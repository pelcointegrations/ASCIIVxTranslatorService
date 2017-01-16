using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading;

using System.IO;
using System.IO.Ports;

using System.Net.Sockets;
using System.Net;
using System.Net.NetworkInformation;

namespace ASCIIEvents
{
    class PTZInfo
    {
        public int pan;
        public int tilt;
        public int zoom;
        public int iris;
        public int focus;

        public PTZInfo()
        {
            Clear();
        }

        public PTZInfo(PTZInfo ptz)
        {
            pan = ptz.pan;
            tilt = ptz.tilt;
            zoom = ptz.zoom;
            iris = ptz.iris;
            focus = ptz.focus;
        }

        public void Clear()
        {
            pan = 0;
            tilt = 0;
            zoom = 0;
            iris = 0;
            focus = 0;
        }

        public bool StoppingPTZ()
        {
            if (pan == 0 && tilt == 0 && zoom == 0 && iris == 0 && focus == 0)
                return true;
            else return false;
        }
    }

    class ASCIIEventHandler
    {
        private static string SdkKey = "C6B57D9440C8DFE461574971CE6A4811EF9CA6D254452E386D2748A464FD5606";
        private static int MAX_KVP_VALUE_SIZE = 2048; // must break up receipt into chunks smaller than this
        private static int MAX_ASCII_COMMAND_LENGTH = 2048; // throw out commands bigger than this

        private CPPCli.VXSystem _vxSystem = null;
        private object _vxSystemLock = new object();

        private string _systemID = string.Empty;
        private List<CPPCli.Monitor> _monitors = null;
        private object _monitorLock = new object();
        private List<CPPCli.DataSource> _datasources = null;
        private object _datasourceLock = new object();
        private List<CPPCli.Situation> _situations = null; //total situation list read from Vx
        private object _situationLock = new object();
        private List<CustomSituation> _customSituations = null;
        private ASCIIEventServerSettings _settings = null;
        private List<Command> _commands = null;
        private List<string> _delimiters = null;
        private List<Alarm> _alarmConfig = null;
        private int _selectedCamera = 0;
        private int _selectedMonitor = 0;
        private int _selectedCell = 0;

        private PTZInfo _currentPTZInfo = new PTZInfo();
        private DateTime _commandDelayUntilTime = DateTime.Now;

        private int _debugLevel;

        private Thread _listenASCIISerialThread = null;
        private Thread _listenASCIIEthernetThread = null;
        private Thread _processPTZCommandsThread = null;
        private Thread _refreshDataThread = null;
        private object _lockPTZCommand = new object();
        private bool _newPTZCommand = false;
        private volatile bool _stopping = false;

        private bool _posMode = false;
        private List<string> _extraPOSdata = new List<string>();

        public ASCIIEventHandler(CustomSituations customSits, 
            ASCIIEventServerSettings settings, 
            ASCIICommandConfiguration asciiCommands, 
            AlarmConfiguration alarmConfiguration)
        {
            try
            {
                _settings = settings;
                _debugLevel = _settings.DebugLevel;

                // Set whether or not to run in POS mode
                if ((_settings.POSSettings != null) && (_settings.POSSettings.POSMode == true))
                {
                    _posMode = true;
                    Trace.WriteLineIf(_debugLevel > 0, "ASCII Interpreter set for POS Mode");
                }

                //moved before InitializeVxWrapper so custom events from xml can be added to vx
                if (customSits != null)
                    _customSituations = customSits.customSituations.ToList();

                // initialize _vxSystem
                Initialize();

                if ((asciiCommands != null) && (asciiCommands.Commands != null))
                {
                    _commands = asciiCommands.Commands.Command.ToList();
                    _delimiters = asciiCommands.Commands.GetDelimiters();
                }

                if (alarmConfiguration != null)
                    _alarmConfig = alarmConfiguration.alarms.ToList();

                _stopping = false;

                if ((_settings.SerialPortSettings != null) && (_settings.SerialPortSettings.PortName != string.Empty))
                {
                    this._listenASCIISerialThread = new Thread(this.ListenASCIISerialThread);
                    this._listenASCIISerialThread.Start();
                }
                if ((_settings.EthernetSettings != null)&&(_settings.EthernetSettings.Port != 0))
                {
                    this._listenASCIIEthernetThread = new Thread(this.ListenASCIIEthernetThread);
                    this._listenASCIIEthernetThread.Start();
                }

                this._processPTZCommandsThread = new Thread(this.ProcessPTZCommandThread);
                this._processPTZCommandsThread.Start();

                this._refreshDataThread = new Thread(this.RefreshDataThread);
                this._refreshDataThread.Start();
            }
            catch (Exception exception)
            {
                Trace.WriteLine(string.Format("Error Initializing ASCIIEventHandler {0}\n{1}", exception.Message, exception.StackTrace));
            }
        }

        ~ASCIIEventHandler()
        {
            // stop monitor thread
            _stopping = true;

            // join it to wait for it to exit
            if (_listenASCIISerialThread != null)
                _listenASCIISerialThread.Join();

            _listenASCIISerialThread = null;

            if (_processPTZCommandsThread != null)
                _processPTZCommandsThread.Join();

            _processPTZCommandsThread = null;

            if (_refreshDataThread != null)
                _refreshDataThread.Join();

            _refreshDataThread = null;

            if (_listenASCIIEthernetThread != null)
                _listenASCIIEthernetThread.Join();

            _listenASCIIEthernetThread = null;
        }

        #region INITIALIZATION OF VXSDK
        private CPPCli.VXSystem GetVxSystem()
        {
            // re-initialize connection if needed
            if ((_vxSystem == null))// || (!_vxCore.IsConnected()))
            {
                lock (_vxSystemLock)
                {
                    _vxSystem = null;
                    ConnectVxSystem(ref _vxSystem, _settings.VxUsername, _settings.VxPassword, _settings.VxCoreAddress, _settings.VxCorePort, true);
                }
            }
            return _vxSystem;
        }

        private void Initialize()
        {
            CPPCli.VXSystem system = GetVxSystem();
            if (system == null)
            {
                Trace.WriteLine("Failed to connect to VideoXpert system at " + _settings.VxCoreAddress);
            }
            else
            {
                lock(_monitorLock)
                {
                    _monitors = system.GetMonitors();
                }
                lock(_datasourceLock)
                {
                    _datasources = system.GetDataSources();
                }
                lock(_situationLock)
                {
                    _situations = system.GetSituations();
                }
                RegisterExternalDevice();

                LoadCustomSituations();
            }
        }

        private void RegisterExternalDevice()
        {
            CPPCli.VXSystem system = GetVxSystem();
            if (system != null)
            {
                string localIp = GetLocalIPv4();
                CPPCli.Device thisDevice = null;
                try
                {
                    List<CPPCli.Device> devices = system.GetDevices();
                    if (devices != null)
                    {
                        thisDevice = devices.Find(x => (x.Ip == localIp && x.Type == CPPCli.Device.Types.External));
                    }
                }
                catch { };
                // if we are not registered, then call AddDevice to register us with the system
                if (thisDevice == null)
                {
                    CPPCli.NewDevice asciiDevice = new CPPCli.NewDevice();
                    asciiDevice.Name = "ASCII Vx Translator Service";
                    //asciiDevice.Id = _settings.IntegrationId;
                    if (!string.IsNullOrEmpty(_settings.EthernetSettings.Address))
                        asciiDevice.Ip = _settings.EthernetSettings.Address;
                    else asciiDevice.Ip = localIp;
                    asciiDevice.ShouldAutoCommission = true;
                    asciiDevice.Type = CPPCli.Device.Types.External;
                    CPPCli.Results.Value ret = system.AddDevice(asciiDevice);
                    if (ret == CPPCli.Results.Value.OK)
                    {
                        try
                        {
                            List<CPPCli.Device> devices = system.GetDevices();
                            if (devices != null)
                            {
                                thisDevice = devices.Find(x => (x.Ip == localIp && x.Type == CPPCli.Device.Types.External && x.Name == "ASCII Vx Translator Service"));
                            }
                        }
                        catch { };
                        if (thisDevice != null)
                        {
                            _settings.IntegrationId = thisDevice.Id;
                            Trace.WriteLineIf(_debugLevel > 0, "Integration registered with Vx: " + thisDevice.Id);
                        }
                        else
                        {
                            Trace.WriteLineIf(_debugLevel > 0, "ERROR: unable to retrieve integrationId from core after registration.");
                        }
                    }
                    else
                    {
                        Trace.WriteLineIf(_debugLevel > 0, "Failed to register Integration with Vx or find previous registration");
                    }
                }
                else
                {
                    _settings.IntegrationId = thisDevice.Id;
                    Trace.WriteLineIf(_debugLevel > 0, "Integration already registered with Vx: " + _settings.IntegrationId);
                }
            }
        }

        /// <summary>
        /// Gets the local Ipv4.
        /// </summary>
        /// <returns>String containing the local Ip Address.</returns>
        /// <param name="networkInterfaceType">Network interface type.</param>
        private string GetLocalIPv4()
        {
            string address = string.Empty;
            var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(i => i.OperationalStatus == OperationalStatus.Up);

            foreach (NetworkInterface networkInterface in networkInterfaces)
            {
                var adapterProperties = networkInterface.GetIPProperties();

                if (adapterProperties.GatewayAddresses.FirstOrDefault() != null)
                {
                    foreach (UnicastIPAddressInformation ip in networkInterface.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            address = ip.Address.ToString();
                            break;
                        }
                    }
                }
                if (!string.IsNullOrEmpty(address))
                    break;
            }

            return address;
        }

        private void LoadCustomSituations()
        {
            if (_customSituations == null)
                return;
            CPPCli.VXSystem system = GetVxSystem();
            if (system == null)
                return;

            bool situationsAddedOrModified = false;
            foreach (CustomSituation custSit in _customSituations)
            {
                CPPCli.Situation vxSit = null;
                try
                {
                    lock(_situationLock)
                    {
                        vxSit = _situations.Find(x => x.Type == custSit.SituationType);
                    }
                }
                catch { };

                if (vxSit == null)
                {
                    CPPCli.NewSituation newSit = new CPPCli.NewSituation();
                    newSit.IsAckNeeded = custSit.AckNeeded;
                    //newSit.AudibleLoopDelay = 2;
                    newSit.UseAudibleNotification = custSit.Audible;
                    //newSit.AudiblePlayCount = 1; //not in custom xml
                    newSit.AutoAcknowledge = custSit.AutoAcknowledge;
                    newSit.ShouldExpandBanner = custSit.DisplayBanner;
                    newSit.ShouldLog = custSit.Log;
                    newSit.Name = custSit.Name;
                    newSit.ShouldNotify = custSit.Notify;
                    newSit.Severity = custSit.Severity;
                    //newSit.SnoozeIntervals = null;
                    //newSit.SourceDeviceId = null; //set when pushing to vx.
                    newSit.Type = custSit.SituationType;
                    CPPCli.Results.Value addRes = system.AddSituation(newSit);
                    if (addRes == CPPCli.Results.Value.OK)
                    {
                        Trace.WriteLineIf(_debugLevel > 0, "Added custom situation: " + newSit.Type);
                        situationsAddedOrModified = true; //at least one was added.
                    }
                    newSit = null;
                }
                else
                {
                    bool modified = false;
                    //Custom Sit is already in the system... 
                    // see if our xml version differs and patch each difference
                    if (custSit.AutoAcknowledge != vxSit.AutoAcknowledge)
                    {
                        vxSit.AutoAcknowledge = custSit.AutoAcknowledge;
                        modified = true;
                    }
                    if (custSit.Audible != vxSit.UseAudibleNotification)
                    {
                        vxSit.UseAudibleNotification = custSit.Audible;
                        modified = true;
                    }
                    if (custSit.DisplayBanner != vxSit.ShouldExpandBanner)
                    {
                        vxSit.ShouldExpandBanner = custSit.DisplayBanner;
                        modified = true;
                    }
                    if (custSit.Log != vxSit.ShouldLog)
                    {
                        vxSit.ShouldLog = custSit.Log;
                        modified = true;
                    }
                    if (custSit.Name != vxSit.Name)
                    {
                        vxSit.Name = custSit.Name;
                        modified = true;
                    }
                    if (custSit.Notify != vxSit.ShouldNotify)
                    {
                        vxSit.ShouldNotify = custSit.Notify;
                        modified = true;
                    }
                    if (custSit.Severity != vxSit.Severity)
                    {
                        vxSit.Severity = custSit.Severity;
                        modified = true;
                    }
                    if (modified)
                    {
                        Trace.WriteLineIf(_debugLevel > 0, "Modified custom situation: " + vxSit.Type);
                        situationsAddedOrModified = true;
                    }
                }
            }
            if (situationsAddedOrModified)
            {
                // refresh situations now that one or more have been added
                lock(_situationLock)
                {
                    _situations = _vxSystem.GetSituations();
                }
            }
        }
        #endregion

        private void ListenASCIISerialThread()
        {
            SerialPort serialPort = null;

            string commandStr = string.Empty;

            while (!_stopping)
            {
                if (serialPort != null)
                {
                    try
                    {
                        if (_posMode)
                        {
                            commandStr += Convert.ToChar(serialPort.ReadChar());

                            if (AtPOSDelimiter(commandStr))
                            {
                                if (FindPOSKeyWord(commandStr))
                                {
                                    InjectPOSEvent(commandStr, true);
                                    commandStr = string.Empty;
                                }
                            }
                            else if (AtLineDelimiter(commandStr))
                            {
                                InjectPOSEvent(commandStr, false);
                            }

                            if (commandStr.Length > _settings.POSSettings.MaxReceiptLength)
                            {
                                commandStr = commandStr.Substring(1); // remove first char
                            }
                        }
                        else
                        {
                            // don't process or read chars until wait time is up
                            if (DateTime.Now > _commandDelayUntilTime)
                            {
                                int rawChar = serialPort.ReadChar();
                                //Trace.WriteIf(_debugLevel > 1, " " + rawChar.ToString("x") + " ");
                                char charRead = Convert.ToChar(rawChar);
                                commandStr += charRead;
                                //Trace.WriteLineIf(_debugLevel > 1, "Char read in : " + charRead);
                                if (FindCommandDelimiter(commandStr))
                                {
                                    string response = string.Empty;

                                    Trace.WriteLineIf(_debugLevel > 0, "Command Delimiter found, Processing Command: " + commandStr);
                                    bool cmdFound = ProcessCommand(commandStr, out response);
                                    if (cmdFound)
                                    {
                                        commandStr = string.Empty;
                                        if ((cmdFound) && (response != string.Empty))
                                        {
                                            Trace.WriteLineIf(_debugLevel > 0, "ProcessCommand response: " + response);
                                            serialPort.Write(response);
                                        }
                                    }
                                }
                                else if (commandStr.Length > MAX_ASCII_COMMAND_LENGTH)
                                {
                                    commandStr = string.Empty; // clear it if it gets too large
                                }
                            }
                            else
                            {
                                Trace.WriteLineIf(_debugLevel > 0, DateTime.Now.ToString() + " Wait command in effect, until " + _commandDelayUntilTime.ToString());
                                Thread.Sleep(1000);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        if (e is TimeoutException)
                        {
                            // throw out any data
                            commandStr = string.Empty;
                        }
                        else // InvalidOperationException - port is not open for reading
                        {
                            Trace.WriteLineIf(_debugLevel > 0, "Serial Port exception: " + e.Message);
                            // close it so we can try opening again
                            if (serialPort != null)
                            {
                                serialPort.Close();
                                serialPort = null;
                            }
                            break;
                        }
                    }
                }
                else
                {
                    serialPort = OpenSerialPort();
                    Thread.Sleep(3000);
                }
            }
            if (serialPort != null)
                serialPort.Close();
        }

        private void ProcessPTZCommandThread()
        {
            while (!_stopping)
            {
                PTZInfo ptz = null;
                Thread.Sleep(50);
                lock (_lockPTZCommand)
                {
                    if (_newPTZCommand)
                    {
                        ptz = new PTZInfo(_currentPTZInfo);
                        _newPTZCommand = false;
                    }
                }
                // do this out of lock so bytes may be written to _currentPTZInfo
                if (ptz != null)
                {
                    if (ptz.StoppingPTZ())
                        SendPTZStop();
                    else SendPTZCommand(ptz);
                }
            }
        }

        private void RefreshDataThread()
        {
            int sleepInterval = 100;
            int intervalMonitorUpdate = 3 * 60 ; // 3 minutes intervals
            int intervalDataSourceUpdate = 4 * 60; // 4 minutes intervals
            int intervalSituationUpdate = 30 * 60; // 30 minutes interval (should not need to do this)

            TimeSpan monitorTimeSpan = new TimeSpan(0, 0, intervalMonitorUpdate);
            TimeSpan dataSourceTimeSpan = new TimeSpan(0, 0, intervalDataSourceUpdate);
            TimeSpan situationTimeSpan = new TimeSpan(0, 0, intervalSituationUpdate);

            DateTime nextMonitorUpdate = DateTime.Now + monitorTimeSpan;
            DateTime nextDataSourceUpdate = DateTime.Now + dataSourceTimeSpan;
            DateTime nextSituationUpdate = DateTime.Now + situationTimeSpan;

            while (!_stopping)
            {       
                Thread.Sleep(sleepInterval);
                DateTime now = DateTime.Now;
                if (now > nextMonitorUpdate)
                {
                    DateTime time = DateTime.Now;
                    Trace.WriteLineIf((_debugLevel > 0), time.ToString() + " Refreshing Monitor Data");
                    List<CPPCli.Monitor> monitors = null;
                    try
                    {
                        CPPCli.VXSystem system = GetVxSystem();
                        if (system != null)
                        {
                            monitors = system.GetMonitors();
                        }
                    }
                    catch { };

                    if (monitors != null)
                    {
                        DateTime timeUpdate = DateTime.Now;
                        Trace.WriteLineIf((_debugLevel > 0), timeUpdate.ToString() + " Update Monitors");
                        lock (_monitorLock)
                        {
                            _monitors = monitors;
                        }
                    }
                    nextMonitorUpdate = DateTime.Now + monitorTimeSpan;
                }

                if (now > nextDataSourceUpdate)
                {
                    DateTime time = DateTime.Now;
                    Trace.WriteLineIf((_debugLevel > 0), time.ToString() + " Refreshing DataSources");
                    List<CPPCli.DataSource> datasources = null;
                    try
                    {
                        CPPCli.VXSystem system = GetVxSystem();
                        if (system != null)
                        {
                            datasources = system.GetDataSources(); ;
                        }
                    }
                    catch { };

                    if (datasources != null)
                    {
                        DateTime timeUpdate = DateTime.Now;
                        Trace.WriteLineIf((_debugLevel > 0), timeUpdate.ToString() + " Update DataSources");
                        lock (_datasourceLock)
                        {
                            _datasources = datasources;
                        }
                    }
                    nextDataSourceUpdate = DateTime.Now + dataSourceTimeSpan;
                }

                if (now > nextSituationUpdate)
                {
                    DateTime time = DateTime.Now;
                    Trace.WriteLineIf((_debugLevel > 0), time.ToString() + " Refreshing Situation Data");
                    List<CPPCli.Situation> situations = null;
                    try
                    {
                        CPPCli.VXSystem system = GetVxSystem();
                        if (system != null)
                        {
                            situations = system.GetSituations();
                        }
                    }
                    catch { };

                    if (situations != null)
                    {
                        DateTime timeUpdate = DateTime.Now;
                        Trace.WriteLineIf((_debugLevel > 0), timeUpdate.ToString() + " Update Situations");
                        lock (_situationLock)
                        {
                            _situations = situations;
                        }
                    }
                    nextSituationUpdate = DateTime.Now + situationTimeSpan;
                }
            }
        }

        private void ListenASCIIEthernetThread()
        {
            UdpClient listener = null;

            string commandStr = string.Empty;
            IPEndPoint anyRemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
            while (!_stopping)
            {
                if (listener != null)
                {
                    try
                    {
                        Byte[] receiveBytes = listener.Receive(ref anyRemoteEndPoint);
                        string receivedStr = Encoding.ASCII.GetString(receiveBytes, 0, receiveBytes.Length);
                        commandStr += receivedStr;
                    }
                    catch (Exception e)
                    {
                        if (e is SocketException)
                        {
                            SocketException sExcept = (SocketException)e;
                            if (sExcept.ErrorCode != 10060) // time out
                            {
                                Trace.WriteLineIf((_debugLevel > 0), "Listener Socket exception: " + e.Message);
                                listener = null; // reopen socket and start over
                            }                             
                        }
                        else
                        {
                            Trace.WriteLineIf((_debugLevel > 0), "Unknown Listener Exception: " + e.Message);
                        }
                    }
                    // don't process commands if waiting
                    if (DateTime.Now > _commandDelayUntilTime)
                    {
                        string command = string.Empty;
                        string remainder = string.Empty;
                        string response = string.Empty;
                        bool cmdFound = false;
                        while (SplitCommandString(commandStr, out command, out remainder))
                        {
                            commandStr = remainder; // keep what is left for next iteration (may be partial)
                            Trace.WriteLineIf(_debugLevel > 0, "Command Delimiter found, Processing Command: " + command);

                            cmdFound = ProcessCommand(command, out response);
                            // if last command was wait command, kick out of while
                            if (_commandDelayUntilTime > DateTime.Now)
                                break;
                        }
                        if ((cmdFound)&&(response != string.Empty))
                        {
                            // todo: send response back
                            Trace.WriteLineIf(_debugLevel > 0, "Command Response: " + response);
                        }
                    }
                    else
                    {
                        //Trace.WriteLineIf(_debugLevel > 0, DateTime.Now.ToString() + " Wait command in effect, until " + _commandDelayUntilTime.ToString());
                        Thread.Sleep(1000);
                    }

                    // sanity check, throw out string if it gets too long, max=?
                    if (commandStr.Length > MAX_ASCII_COMMAND_LENGTH)
                        commandStr = string.Empty;
                }
                else
                {
                    listener = OpenUDPListener();
                    Thread.Sleep(3000);
                }
            }

            if (listener != null)
                listener.Close();
        }

        private UdpClient OpenUDPListener()
        {
            try
            {
                IPAddress address = IPAddress.Any;
                if (_settings.EthernetSettings.Address != string.Empty)
                {
                    address = IPAddress.Parse(_settings.EthernetSettings.Address);
                }
                IPEndPoint endPoint = new IPEndPoint(address, _settings.EthernetSettings.Port);
                UdpClient listener = new UdpClient(endPoint);
                listener.Client.ReceiveTimeout = 500; // 500 ms
                return listener;
            }
            catch (Exception e)
            {
                Trace.WriteLineIf((_debugLevel > 0), "Failed to open UDP port " + _settings.EthernetSettings.Port + " Exception: " + e.Message);
                return null; 
            }
        }

        #region ASCII_SERIAL_PORT
        private SerialPort OpenSerialPort()
        {
            SerialPort serialPort = new SerialPort();
            try
            {
                serialPort = new SerialPort();
                serialPort.PortName = _settings.SerialPortSettings.PortName;
                serialPort.BaudRate = _settings.SerialPortSettings.BaudRate;
                serialPort.DataBits = _settings.SerialPortSettings.DataBits;
                serialPort.Parity = GetParityFromSettings(_settings.SerialPortSettings.Parity);
                serialPort.StopBits = GetStopBitsFromSettings(_settings.SerialPortSettings.StopBits);
                serialPort.Handshake = Handshake.None;
                serialPort.ReadTimeout = 500; // todo: what values
                serialPort.WriteTimeout = 500; // todo: what value
                serialPort.Open();
                Trace.WriteLineIf((_debugLevel > 0), "Successfully opened serial port " + _settings.SerialPortSettings.PortName + ",Baud " 
                    + serialPort.BaudRate + ",Databits " + serialPort.DataBits + ",Parity " + serialPort.Parity + ",StopBits " + serialPort.StopBits);
            }
            catch (Exception e)
            {                
                Trace.WriteLineIf((_debugLevel > 0), "Failed to open serial port " + _settings.SerialPortSettings.PortName + " Exception: " + e.Message);
                serialPort = null;
            }
            return serialPort;
        }

        private Parity GetParityFromSettings(string parity)
        {
            if (parity.ToLower() == "none")
                return Parity.None;
            if (parity.ToLower() == "odd")
                return Parity.Odd;
            if (parity.ToLower() == "even")
                return Parity.Even;
            if (parity.ToLower() == "mark")
                return Parity.Mark;
            if (parity.ToLower() == "space")
                return Parity.Space;
            return Parity.None;
        }

        private StopBits GetStopBitsFromSettings(string stopBits)
        {
            if (stopBits.ToLower() == "0")
                return StopBits.None;
            if (stopBits.ToLower() == "1")
                return StopBits.One;
            if (stopBits.ToLower() == "2")
                return StopBits.Two;
            if (stopBits.ToLower() == "1.5")
                return StopBits.OnePointFive;
            return StopBits.None;
        }

        #endregion

        #region STATUS_AND_DEBUG
        /// <summary>
        /// Get status of Event Handler
        /// </summary>
        /// <returns>String containing the status.</returns>
        public string GetStatus()
        {
            string status = "ASCIIEventHandler status: \n";
            if (!_stopping)
                status += "   Listening for ASCII Events\n";
            CPPCli.VXSystem system = GetVxSystem();
            if (system != null)
            {
                status += "   Connected to VideoXpert";
            }
            else status += "   NOT connected to VideoXpert";

            return status;
        }

        /// <summary>
        /// Set Debug
        /// </summary>
        /// <param name="level">level 0 = off</param>
        public void SetDebug(int level)
        {
            _debugLevel = level;
        }

        /// <summary>
        /// Processes a test file containing POS data as if it were coming in through the serial port
        /// </summary>
        /// <param name="filename">name of file containing POS data</param>
        public void ProcessTestFile(string fileName)
        {
            string commandStr = string.Empty;
            StreamReader reader;
            reader = new StreamReader(fileName);

            if (reader != null)
            {
                Random rnd = new Random();
                while (!reader.EndOfStream)
                {
                    char ch = (char)reader.Read();

                    try
                    {
                        if (_posMode)
                        {
                            commandStr += ch;
                            if (AtPOSDelimiter(commandStr))
                            {
                                if (FindPOSKeyWord(commandStr))
                                {
                                    InjectPOSEvent(commandStr, true);
                                    commandStr = string.Empty;
                                }
                            }
                            else if (AtLineDelimiter(commandStr))
                            {
                                InjectPOSEvent(commandStr, false);
                                // wait 1 to 4 seconds before looking for next line
                                int waitTime = rnd.Next(1, 5) * 1000;
                                Thread.Sleep(waitTime);
                            }

                            if (commandStr.Length > _settings.POSSettings.MaxReceiptLength)
                            {
                                commandStr = commandStr.Substring(1); // remove first char
                            }
                        }
                        else
                        {
                            // don't process or read chars until wait time is up
                            if (DateTime.Now > _commandDelayUntilTime)
                            {
                                commandStr += ch;
                                if (FindCommandDelimiter(commandStr))
                                {
                                    Trace.WriteLineIf(_debugLevel > 0, "Command Delimiter found, Processing Command: " + commandStr);
                                    string response = string.Empty;
                                    bool cmdFound = ProcessCommand(commandStr, out response);
                                    commandStr = string.Empty;
                                    if ((cmdFound) && (response != string.Empty))
                                        Trace.WriteLineIf(_debugLevel > 0, "Command Respose: " + response);
                                }
                            }
                            else
                            {
                                Trace.WriteLineIf(_debugLevel > 0, DateTime.Now.ToString() + " Wait command in effect, until " + _commandDelayUntilTime.ToString());
                                Thread.Sleep(1000);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Trace.WriteLineIf(_debugLevel > 0, " Exception in ProcessTestFile " + e.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Processes a test command as if it were coming in through serial port
        /// </summary>
        /// <param name="testCommand">command to interpret</param>
        /// <returns>String containing a response to the command (if any).</returns>
        public string ProcessTestCommand(string testCommand)
        {
            string command = string.Empty;
            string remainder = string.Empty;
            while (SplitCommandString(testCommand, out command, out remainder))
            {
                testCommand = remainder; // keep what is left for next iteration
                if (DateTime.Now > _commandDelayUntilTime)
                {
                    Trace.WriteLineIf(_debugLevel > 0, "Command Delimiter found, Processing Command: " + command);

                    string response = string.Empty;
                    bool cmdFound = ProcessCommand(command, out response);
                    if ((cmdFound) && (response != string.Empty))
                        Trace.WriteLineIf(_debugLevel > 0, "Command Response: " + response);
                }
                else // wait command in effect
                {
                    Trace.WriteLineIf(_debugLevel > 0, "Wait command in effect, tossing out test command");
                    break;
                }
            }
            if (testCommand == string.Empty)
                return "All Commands Processed";
            else return "Command remainder: " + testCommand;
        }

        /// <summary>
        /// Get datasource information
        /// </summary>
        /// <param name="partialCameraName">filter for a particular resource if desired</param>
        /// <returns>CR LF delimited String containing datasource information.</returns>
        public string GetDataSourceInfo(string partialCameraName)
        {
            string data = string.Empty;
            try
            {
                lock(_datasourceLock)
                {
                    if (string.IsNullOrEmpty(partialCameraName))
                    {
                        foreach (CPPCli.DataSource dataSource in _datasources)
                        {
                            data += "Camera " + dataSource.Number + " : " + dataSource.Name + "\r\n";
                            data += "Id   : " + dataSource.Id + "\r\n";
                        }
                    }
                    else
                    {
                        CPPCli.DataSource dataSource = _datasources.Find(x => x.Name.Contains(partialCameraName));
                        data += "Camera " + dataSource.Number + " : " + dataSource.Name + "\r\n";
                        data += "Id   : " + dataSource.Id + "\r\n";
                    }
                }
            }
            catch
            {
                data = "Camera " + partialCameraName + "  Not Found";
            }

            return data;
        }

        /// <summary>
        /// Get monitor information
        /// </summary>
        /// <param name="partialMonitorName">filter for a particular monitor if desired</param>
        /// <returns>CR LF delimited String containing monitor information.</returns>
        public string GetMonitorInfo(string partialMonitorName)
        {
            string data = string.Empty;
            try
            {
                lock(_monitorLock)
                {
                    if (string.IsNullOrEmpty(partialMonitorName))
                    {
                        foreach (CPPCli.Monitor monitor in _monitors)
                        {
                            data += "Monitor " + monitor.Number + " : " + monitor.Name + "\r\n";
                            data += "Id   : " + monitor.Id + "\r\n";
                        }
                    }
                    else
                    {
                        CPPCli.Monitor monitor = _monitors.Find(x => x.Name.Contains(partialMonitorName));
                        data += "Monitor " + monitor.Number + " : " + monitor.Name + "\r\n";
                        data += "Id   : " + monitor.Id + "\r\n";
                    }
                }
            }
            catch
            {
                data = "Monitor " + partialMonitorName + "  Not Found";
            }

            return data;
        }

        /// <summary>
        /// Get situation type information
        /// </summary>
        /// <returns>String array containing all situation types.</returns>
        public string[] GetSituationTypes()
        {
            string[] situations = null;

            lock (_situationLock)
            {
                if (_situations != null)
                {
                    int count = _situations.Count;
                    situations = new string[count];
                    int i = 0;
                    foreach (CPPCli.Situation sit in _situations)
                    {
                        situations[i] = sit.Type;
                        i++;
                    }
                }
            }
            return situations;
        }
        #endregion

        #region ASCII COMMAND HANDLING ROUTINES

        private int FindNextDelimiter(string command, int startIndex, out string delimiter)
        {
            int delimIndex = -1;
            string lastDelimiterFound = string.Empty;
            foreach (string delim in _delimiters)
            {
                try
                {
                    bool done = false;
                    int tempIndex = 0;
                    while (!done)
                    {
                        tempIndex = command.IndexOf(delim, tempIndex);
                        // delimiter can't be at the start
                        if (tempIndex > 0)
                        {
                            // if this delimiter is not escaped
                            if (command[tempIndex - 1] != '\\')
                            {
                                // if we have not found one, or this one is before the last we found
                                if ((delimIndex == -1) || (delimIndex > tempIndex))
                                {
                                    delimIndex = tempIndex;
                                    lastDelimiterFound = delim;
                                }
                                done = true;
                            }
                            else // handle possible preset command (same as escape char)
                            {
                                // \ is preset, so \a is a valid command - so we may need to accept it
                                // check specifically for x\a where x is a number
                                if (((tempIndex - 2) >= 0)&&(Char.IsNumber(command[tempIndex-2])))
                                {
                                    if (command[tempIndex] == 'a')
                                    {
                                        // if we have not found one, or this one is before the last we found
                                        if ((delimIndex == -1) || (delimIndex > tempIndex))
                                        {
                                            delimIndex = tempIndex;
                                            lastDelimiterFound = delim;
                                        }
                                        done = true;
                                    }
                                }
                            }
                        }
                        else if (tempIndex == -1)
                        {
                            done = true; // not found
                        }
                        tempIndex++; // start looking after the last found index for non escaped
                    }
                }
                catch { };
            }
            if (delimIndex != -1)
            {
                delimiter = lastDelimiterFound;
                return delimIndex;
            }
            else // no delimiters found
            {
                delimiter = string.Empty;
                return -1;
            }
        }

        // since mulitple commands or even partial commands might be received, this
        // splits off first command from command string with remainder (if any) in second string
        private bool SplitCommandString(string commandStr, out string command, out string remainder)
        {
            string delimiter = string.Empty;
            int nextCommandDelimiter = FindNextDelimiter(commandStr, 0, out delimiter);
            if (nextCommandDelimiter == -1)
            {
                command = string.Empty;
                remainder = string.Empty;
                return false;
            }
            else
            {
                int delimSize = delimiter.Length;
                int endDelim = nextCommandDelimiter + delimSize - 1;
                int cmdLength = endDelim + 1;
                int remainderLength = commandStr.Length - endDelim - 1;
                // if whole thing is just one command
                if (cmdLength == commandStr.Length)
                {
                    command = commandStr;
                    remainder = string.Empty;
                }
                else if (endDelim < commandStr.Length)
                {
                    command = commandStr.Substring(0, cmdLength);
                    remainder = commandStr.Substring(cmdLength, remainderLength);
                }
                else // end of delim cannot exceed string length
                {
                    command = string.Empty;
                    remainder = string.Empty;
                    return false;
                }
                return true;
            }
        }

        // this function relies on reading one char at a time and sending string through this routine
        // as the command is read in (useful for serial)
        private bool FindCommandDelimiter(string command)
        {
            bool found = false;

            foreach (string delim in _delimiters)
            {
                if (command.EndsWith(delim))
                {
                    // if the delimiter is not escaped with '\' then this is the end of the command
                    int indx = command.LastIndexOf(delim);
                    indx = indx - 1;
                    if (indx >= 0)
                    {
                        char prevChar = command[indx];
                        if (prevChar != '\\')
                        {
                            found = true;
                            break;
                        }
                        // \ is preset, so \a is a valid command - so we may need to accept it
                        // check specifically for x\a where x is a number
                        else if (((indx - 1) >= 0) && (Char.IsNumber(command[indx - 1])))
                        {
                            if (command[indx + 1] == 'a')
                            {
                                found = true;
                                break;
                            }
                        }
                    }
                    else
                    {
                        found = false;
                        break;
                    }
                }
            }
            return found;
        }

        private string RemoveEscapeCharacters(string dataStr)
        {
            string retString = dataStr;

            foreach (string delim in _delimiters)
            {
                string escapedDelim = "\\" + delim;

                if (retString.Contains(escapedDelim))
                {
                    retString = retString.Replace(escapedDelim, delim);
                }
            }
            return retString;
        }

        private bool ProcessCommand(string commandStr, out string response)
        {
            bool cmdFound = false;
            response = string.Empty;

            // must be at least a command and delimiter
            if (commandStr.Length < 2)
            {
                Trace.WriteLineIf(_debugLevel > 0, "ProcessCommand: commandStr length < 2, exiting " + commandStr.Length);
                return cmdFound;
            }
            Command command = new Command();
            string parameter = string.Empty;
            if (GetCommandFromString(commandStr, out command, out parameter))
            {
                response = HandleCommand(command, parameter);
                cmdFound = true;
            }

            return cmdFound;
        }

        private bool GetCommandFromString(string commandStr, out Command command, out string parameter)
        {
            try
            {
                List<Command> possibleCommands = new List<Command>();

                // look for special case Seek
                Command seekCommand = _commands.Find(x => x.Name == "Seek");
                if (commandStr.StartsWith(seekCommand.Value))
                {
                    string paramStr = string.Empty;
                    if (ParseAsCommand(seekCommand, commandStr, out paramStr))
                    {
                        command = seekCommand;
                        parameter = paramStr;
                        return true;
                    }
                }
                possibleCommands = _commands.FindAll(x => commandStr.Contains(x.Value));

                foreach (Command cmd in possibleCommands)
                {
                    string paramStr = string.Empty;
                    if (ParseAsCommand(cmd, commandStr, out paramStr))
                    {
                        command = cmd;
                        parameter = paramStr;
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                Trace.WriteLineIf(_debugLevel > 0, "Error in GetCommandFromString: " + e.Message);
            };

            // unable to parse
            command = null;
            parameter = string.Empty;
            return false;
        }

        private bool ParseAsCommand(Command cmd, string commandStr, out string outParam)
        {
            bool earlyExit = false;
            outParam = string.Empty;
            bool specialParam = false;

            // look for special char ~ which signifies to stop (stop iris, focus, pan, tilt)
            if (commandStr.StartsWith("~"))
            {
                specialParam = true;
            }

            int cmdStart = commandStr.IndexOf(cmd.Value, 0);
            int cmdLength = cmd.Value.Length;
            int cmdEnd = cmdStart + cmdLength;
            int delimiterPos = commandStr.LastIndexOf(cmd.Delimiter);



            // we can set commandIndex precisely for a cmd that comes after parameter - it
            // will end one character before delimiter and we know the size of the cmd we are looking for
            if ((specialParam) || ((cmd.Parameter != null) && (cmd.Parameter.Position == "before")))
            {
                int end = delimiterPos - 1;
                int start = end - (cmdLength - 1);
                if (start < 0)
                    earlyExit = true;
                else
                {
                    cmdEnd = end;
                    cmdStart = start;
                }
            }

            // if delimiter is not at the end of the command string there is an issue
            // delimiter can't appear anywhere else in command.
            if (delimiterPos != commandStr.Length - 1)
                earlyExit = true;            
            // If end of command that we are comparing to is past end of string exit
            if (cmdEnd > commandStr.Length - 1)
                earlyExit = true;

            if (earlyExit)
            {
                outParam = string.Empty;
                return false;
            }

            if ((specialParam) || (cmd.Parameter != null))
            {
                string paramString = string.Empty;
                // if parameter position is before command
                if ((specialParam) || (cmd.Parameter.Position == "before"))
                {
                    int length = cmdStart;
                    if (length > 0)
                        paramString = commandStr.Substring(0, length);
                }
                // if parameter comes after command
                else if (cmd.Parameter.Position == "after")
                {
                    int length = delimiterPos - cmdEnd;
                    if (length > 0)
                        paramString = commandStr.Substring(cmdEnd, length);
                }

                if (paramString != string.Empty)
                {
                    if ((! specialParam) && (cmd.Parameter.Type.ToLower().Contains("int")))
                    {
                        try
                        {
                            int temp = Convert.ToInt32(paramString);
                        }
                        catch 
                        {
                            outParam = string.Empty;
                            return false;
                        };
                    }
                    outParam = RemoveEscapeCharacters(paramString);
                    return true;
                }
            }
            else // command has no parameters
            {
                // if this is correct command, it should be the exact length of the cmd and delimiter
                int length = cmd.Value.Length + cmd.Delimiter.Length;
                if (commandStr.Length == length)
                    return true;
            }

            outParam = string.Empty;
            return false;
        }

        private string HandleCommand(Command command, string parameter)
        {
            string response = string.Empty;
            int paramInt = 0;
            try
            {
                if (parameter == "~")
                    paramInt = 0;
                else paramInt = Convert.ToInt32(parameter);
            }
            catch { };

            Trace.WriteLine("Received Command: " + command.Name + "  " + parameter);

            if ((command.Parameter != null)&&(command.Parameter.Type.ToLower().Contains("int")))
            {
                if ((parameter != "~") && (paramInt < command.Parameter.Min) || (paramInt > command.Parameter.Max))
                {
                    Trace.WriteLineIf(_debugLevel > 0, "Command " + command.Name + ": param " + paramInt.ToString() + " out of bounds");
                    return response;
                }
            }

            switch (command.Name)
            {
                case "SelectMonitor":
                    SelectMonitor(paramInt);
                    break;
                case "SelectCell":
                    SelectCell(paramInt);
                    break;
                case "SelectCamera":
                    SelectCamera(paramInt);
                    break;
                case "NextCamera":
                    NextCamera();
                    break;
                case "PreviousCamera":
                    PreviousCamera();
                    break;
                case "SingleCameraMode":
                    SingleCameraMode();
                    break;
                case "CameraMode2x2":
                    CameraMode2x2(paramInt);
                    break;
                case "CameraMode3x3":
                    CameraMode3x3(paramInt);
                    break;
                case "CameraMode4x4":
                    CameraMode4x4(paramInt);
                    break;
                case "SetCameraLayout":
                    CPPCli.Monitor.Layouts layout = (CPPCli.Monitor.Layouts)(paramInt - 1); // 0 - 17, valid values
                    SetCameraLayout(layout);
                    break;
                case "Play":
                    Play(paramInt);
                    break;
                case "Stop":
                    Stop();
                    break;
                case "Pause":
                    Pause(paramInt);
                    break;
                case "FastForward":
                    FastForward(paramInt);
                    break;
                case "Rewind":
                    Rewind(paramInt);
                    break;
                case "Seek":
                    Seek(parameter);
                    break;
                case "ToggleLive":
                    ToggleLive();
                    break;
                case "PanLeft":
                    PanLeft(paramInt);
                    break;
                case "PanRight":
                    PanRight(paramInt);
                    break;
                case "TiltUp":
                    TiltUp(paramInt);
                    break;
                case "TiltDown":
                    TiltDown(paramInt);
                    break;
                case "Zoom":
                    Zoom(parameter);
                    break;
                case "Wide":
                    Wide(parameter);
                    break;
                case "StopPTZ":
                    StopPTZ();
                    break;
                case "ExecutePattern":
                    ExecutePattern(paramInt);
                    break;
                case "GotoPreset":
                    GotoPreset(paramInt);
                    break;
                case "FocusNear":
                    FocusNear(parameter);
                    break;
                case "FocusFar":
                    FocusFar(parameter);
                    break;
                case "IrisOpen":
                    IrisOpen(parameter);
                    break;
                case "IrisClose":
                    IrisClose(parameter);
                    break;
                case "TriggerAlarm":
                    TriggerAlarm(paramInt);
                    break;
                case "ClearAlarm":
                    ClearAlarm(paramInt);
                    break;
                case "Wait":
                    Wait(paramInt);
                    break;
                case "AuxOn":
                    AuxOn(paramInt);
                    break;
                case "AuxOff":
                    AuxOff(paramInt);
                    break;
            }
            return response;
        }

        private void SelectMonitor(int monitor)
        {
            _selectedMonitor = monitor;
            _selectedCell = 0;
            _selectedCamera = GetCameraInCell(_selectedMonitor, _selectedCell);
        }

        private void SelectCell(int cell)
        {
            // bounds checked before it gets here
            _selectedCell = cell - 1; // 0 based
            _selectedCamera = GetCameraInCell(_selectedMonitor, _selectedCell);
        }

        private void SelectCamera(int camera)
        {
            // bounds checked before it gets here
            _selectedCamera = camera;
            DisplayCameraOnMonitor(_selectedCamera, _selectedMonitor, _selectedCell);
        }

        private void NextCamera()
        {
            if (CameraWithinBounds(_selectedCamera + 1))
            { 
                _selectedCamera++;
                DisplayCameraOnMonitor(_selectedCamera, _selectedMonitor, _selectedCell);
            }
        }

        private void PreviousCamera()
        {
            if (CameraWithinBounds(_selectedCamera - 1))
            {
                _selectedCamera--;
                DisplayCameraOnMonitor(_selectedCamera, _selectedMonitor, _selectedCell);
            }            
        }

        private void SingleCameraMode()
        {
            _selectedCell = 0;
            CPPCli.Monitor.Layouts layout = CPPCli.Monitor.Layouts.CellLayout1x1;
            SetCameraLayout(layout);
        }

        private void CameraMode2x2(int param)
        {
            _selectedCell = param - 1;
            CPPCli.Monitor.Layouts layout = CPPCli.Monitor.Layouts.CellLayout2x2;
            SetCameraLayout(layout);
        }

        private void CameraMode3x3(int param)
        {
            _selectedCell = param - 1;
            CPPCli.Monitor.Layouts layout = CPPCli.Monitor.Layouts.CellLayout3x3;
            SetCameraLayout(layout);
        }

        private void CameraMode4x4(int param)
        {
            _selectedCell = param - 1;
            CPPCli.Monitor.Layouts layout = CPPCli.Monitor.Layouts.CellLayout4x4;
            SetCameraLayout(layout);
        }

        private void SetCameraLayout(CPPCli.Monitor.Layouts layout)
        {
            if (!SetLayout(_selectedMonitor, layout))
                Trace.WriteLineIf(_debugLevel > 0, "Failed to set Monitor " + _selectedMonitor + " to layout " + layout.ToString());
            else Trace.WriteLineIf(_debugLevel > 0, "Monitor " + _selectedMonitor + " layout set to " + layout.ToString());
        }

        private void Play(int camera)
        {
            if (camera != _selectedCamera)
            {
                _selectedCamera = camera;
                DisplayCameraOnMonitor(_selectedCamera, _selectedMonitor, _selectedCell);
            }
            else
                ChangePlaySpeed(_selectedCamera, _selectedMonitor, _selectedCell, 1);
        }

        private void Stop()
        {
            Disconnect(_selectedCamera, _selectedMonitor, _selectedCell);
        }

        private void Pause(int camera)
        {
            ChangePlaySpeed(_selectedCamera, _selectedMonitor, _selectedCell, 0);
        }

        private void FastForward(int speed)
        {
            ChangePlaySpeed(_selectedCamera, _selectedMonitor, _selectedCell, speed);
        }

        private void Rewind(int speed)
        {
            ChangePlaySpeed(_selectedCamera, _selectedMonitor, _selectedCell, -speed);
        }

        private void Seek(string datetime)
        {
            try
            {
                DateTime time = DateTime.Parse(datetime);
                Seek(_selectedCamera, _selectedMonitor, _selectedCell, time);
            }
            catch
            {
                Trace.WriteLineIf(_debugLevel > 0, "Seek failed - unable to parse time: " + datetime);
            }
            
        }

        private void ToggleLive()
        {
            GoToLive(_selectedCamera, _selectedMonitor, _selectedCell);
        }

        private void PanLeft(int speed)
        {
            lock(_lockPTZCommand)
            {
                _currentPTZInfo.pan = ASCIISpeedToVxSpeed(-speed);
                _newPTZCommand = true;
            }
        }

        private void PanRight(int speed)
        {
            lock (_lockPTZCommand)
            {
                _currentPTZInfo.pan = ASCIISpeedToVxSpeed(speed);
                _newPTZCommand = true;
            }
        }

        private void TiltUp(int speed)
        {
            lock (_lockPTZCommand)
            {
                _currentPTZInfo.tilt = ASCIISpeedToVxSpeed(speed);
                _newPTZCommand = true;
            }
        }

        private void TiltDown(int speed)
        {
            lock (_lockPTZCommand)
            {
                _currentPTZInfo.tilt = ASCIISpeedToVxSpeed(-speed);
                _newPTZCommand = true;
            }
        }

        private void Zoom(string param)
        {
            lock (_lockPTZCommand)
            {
                if (param == "~")
                _currentPTZInfo.zoom = 0;
                else
                    _currentPTZInfo.zoom = 1;
                _newPTZCommand = true;
            }
        }

        private void Wide(string param)
        {
            lock (_lockPTZCommand)
            {

                if (param == "~")
                _currentPTZInfo.zoom = 0;
                else
                    _currentPTZInfo.zoom = -1;
                _newPTZCommand = true;
            }
        }

        private void StopPTZ()
        {
            lock (_lockPTZCommand)
            {
                _currentPTZInfo.Clear();
                _newPTZCommand = true;
            }
        }

        private void ExecutePattern(int pattern)
        {
            SendGotoPattern(pattern);
        }

        private void GotoPreset(int preset)
        {
            SendGotoPreset(preset);
        }

        private void FocusNear(string param)
        {
            if (param == "~")
                _currentPTZInfo.focus = 0;
            else
                _currentPTZInfo.focus = -1;
            SendFocusCommand();
        }

        private void FocusFar(string param)
        {
            if (param == "~")
                _currentPTZInfo.focus = 0;
            else
                _currentPTZInfo.focus = 1;
            SendFocusCommand();
        }

        private void IrisOpen(string param)
        {
            if (param == "~")
                _currentPTZInfo.iris = 0;
            else
                _currentPTZInfo.iris = 1;
            SendIrisCommand();
        }

        private void IrisClose(string param)
        {
            if (param == "~")
                _currentPTZInfo.iris = 0;
            else
                _currentPTZInfo.iris = -1;
            SendIrisCommand();
        }

        private void TriggerAlarm(int alarmNumber)
        {
            Alarm alarm = null;
            // bounds checked before it gets here
            try
            {
                alarm = _alarmConfig.Find(x => x.Number == alarmNumber);
            }
            catch
            {
                Trace.WriteLineIf(_debugLevel > 0, "x.Number field missing from alarm cfg xml");
            }
            if (alarm != null)
            {
                vxSituation enableSit = null;
                try
                {
                    List<vxSituation> sitList = alarm.Situations.ToList();
                    enableSit = sitList.Find(x => x.AlarmState == 1);
                }
                catch
                {
                    Trace.WriteLineIf(_debugLevel > 0, "x.AlarmState field missing from situations in xml");
                }
                if (enableSit != null)
                {
                    CPPCli.Situation vxSit = null;
                    try
                    {
                        //trigger alarm found in our alarm config xml
                        //now check if it's enableSit is in our known Vx situations
                        lock(_situationLock)
                        {
                            vxSit = _situations.Find(x => x.Type == enableSit.Type);
                        }
                    }
                    catch
                    {
                        Trace.WriteLineIf(_debugLevel > 0, "x.SituationType field missing from Vx sitlist");
                    }
                    // if Vx knows the situation type, inject the event
                    if (vxSit != null)
                    {
                        //found our enable sit in the Vx Situations.
                        Trace.WriteLineIf(_debugLevel > 0, "Found alarm sit type in Vx sitlist, " + vxSit.Type);
                        CPPCli.NewEvent newEvent = new CPPCli.NewEvent();
                        newEvent.GeneratorDeviceId = _settings.IntegrationId;   // unique identifier for this integration
                        if (string.IsNullOrEmpty(alarm.SourceDeviceId) || alarm.SourceDeviceId == "USE_INTEGRATION_ID")
                            newEvent.SourceDeviceId = _settings.IntegrationId;
                        else
                            newEvent.SourceDeviceId = alarm.SourceDeviceId;
                        newEvent.SituationType = vxSit.Type;
                        newEvent.Time = DateTime.UtcNow;

                        List<KeyValuePair<string, string>> properties = new List<KeyValuePair<string, string>>();
                        foreach (SituationProperty prop in enableSit.properties)
                        {
                            KeyValuePair<string, string> kvp = new KeyValuePair<string, string>(prop.Key, prop.Value);
                            properties.Add(kvp);
                        }

                        if (properties.Count > 0)
                            newEvent.Properties = properties;

                        ForwardEventToVx(newEvent);
                    }
                    else
                    {
                        // situation not found.
                        Trace.WriteLineIf(_debugLevel > 0, "\nEnable Sit type, " + enableSit.Type + ", not found in Vx sitlist");
                    }
                }
                else
                {
                    Trace.WriteLineIf(_debugLevel > 0, "Alarm enable situation not found in config xml for Alarm " + alarmNumber);
                }
            }
            else
            {
                Trace.WriteLineIf(_debugLevel > 0, "Alarm " + alarmNumber + " not found in alarm cfg xml.");
            }
        }

        private void ClearAlarm(int alarmNumber)
        {
            Alarm alarm = null;
            // bounds checked before it gets here
            try
            {
                alarm = _alarmConfig.Find(x => x.Number == alarmNumber);
            }
            catch
            {
                Trace.WriteLineIf(_debugLevel > 0, "x.Number field missing from alarm cfg xml");
            }
            if (alarm != null)
            {
                vxSituation clearSit = null;
                try
                {
                    List<vxSituation> sitList = alarm.Situations.ToList();
                    clearSit = sitList.Find(x => x.AlarmState == 0);
                }
                catch
                {
                    Trace.WriteLineIf(_debugLevel > 0, "x.AlarmState field missing from situations in xml");
                }
                if (clearSit != null)
                {
                    CPPCli.Situation vxSit = null;
                    try
                    {
                        //clear alarm found in our alarm config xml
                        //now check if it's clearSit is in our known Vx situations
                        lock(_situationLock)
                        {
                            vxSit = _situations.Find(x => x.Type == clearSit.Type);
                        }
                    }
                    catch
                    {
                        Trace.WriteLineIf(_debugLevel > 0, "x.SituationType field missing from Vx sitlist");
                    }
                    // if Vx knows the situation type, inject the event
                    if (vxSit != null)
                    {
                        //found our enable sit in the Vx Situations.
                        Trace.WriteLineIf(_debugLevel > 0, "Found alarm clear sit type in Vx sitlist, " + vxSit.Type);
                        CPPCli.NewEvent newEvent = new CPPCli.NewEvent();
                        newEvent.GeneratorDeviceId = _settings.IntegrationId;   // unique identifier for this integration
                        if (string.IsNullOrEmpty(alarm.SourceDeviceId) || alarm.SourceDeviceId == "USE_INTEGRATION_ID")
                            newEvent.SourceDeviceId = _settings.IntegrationId;
                        else
                            newEvent.SourceDeviceId = alarm.SourceDeviceId;
                        newEvent.SituationType = vxSit.Type;
                        newEvent.Time = DateTime.UtcNow;

                        List<KeyValuePair<string, string>> properties = new List<KeyValuePair<string, string>>();
                        foreach (SituationProperty prop in clearSit.properties)
                        {
                            KeyValuePair<string, string> kvp = new KeyValuePair<string, string>(prop.Key, prop.Value);
                            properties.Add(kvp);
                        }

                        if (properties.Count > 0)
                            newEvent.Properties = properties;

                        ForwardEventToVx(newEvent);
                    }
                    else
                    {
                        //Situation not found
                        Trace.WriteLineIf(_debugLevel > 0, "\nClear Sit type, " + clearSit.Type + ", not found in Vx sitlist");
                    }
                }
                else
                {
                    Trace.WriteLineIf(_debugLevel > 0, "Alarm clear situation not found in config xml for Alarm " + alarmNumber);
                }
            }
            else
            {
                Trace.WriteLineIf(_debugLevel > 0, "Alarm " + alarmNumber + " not found in alarm cfg xml.");
            }
        }

        private void Wait(int seconds)
        {
            // hopefully the max doesn't get set to something unreasonable
            _commandDelayUntilTime = DateTime.Now + new TimeSpan(0, 0, seconds);

            Trace.WriteLineIf(_debugLevel > 0, "At " + DateTime.Now.ToString() + " Command Delay until " + _commandDelayUntilTime.ToString());
        }

        private void AuxOn(int preset)
        {
            // not supported in Vx yet (as of 1.12)
        }

        private void AuxOff(int preset)
        {
            // not supported in Vx yet (as of 1.12)
        }

        #endregion

        private bool CameraWithinBounds(int cameraNumber)
        {
            try
            {
                Command cmd = _commands.Find(x => x.Name == "SelectCamera");
                if ((cameraNumber >= cmd.Parameter.Min) && (cameraNumber <= cmd.Parameter.Max))
                    return true;
            }
            catch { };
            return false;
        }

        private void GoToLive(int cameraNumber, int monitorNumber, int cellNumber)
        {
            // can't set DateTime to null, so set it equal to DateTime.MinValue
            Seek(cameraNumber, monitorNumber, cellNumber, new DateTime());
        }

        private void Disconnect(int cameraNumber, int monitorNumber, int cellNumber)
        {
            CPPCli.DataSource camera = GetCamera(cameraNumber);
            CPPCli.Monitor monitor = GetMonitor(monitorNumber);
            if (camera != null && monitor != null)
            {
                if (monitor.MonitorCells.Count > cellNumber)
                {
                    CPPCli.MonitorCell cell = monitor.MonitorCells[cellNumber];
                    cell.Disconnect();
                }
            }
        }

        private void Seek(int cameraNumber, int monitorNumber, int cellNumber, DateTime time)
        {
            CPPCli.DataSource camera = GetCamera(cameraNumber);
            CPPCli.Monitor monitor = GetMonitor(monitorNumber);
            if (camera != null && monitor != null)
            {
                if (monitor.MonitorCells.Count > cellNumber)
                {
                    CPPCli.MonitorCell cell = monitor.MonitorCells[cellNumber];
                    // DateTime.MinValue used to signal going to live
                    if (time == DateTime.MinValue)
                    {
                        cell.GoToLive();
                        Trace.WriteLineIf(_debugLevel > 0, "Camera " + cameraNumber + " LIVE");
                    }
                    else
                    {
                        DateTime temptime = cell.Time;
                        cell.Time = time;
                        Trace.WriteLineIf(_debugLevel > 0, "Camera " + cameraNumber + " SEEK TO " + time.ToString());
                        Trace.WriteLineIf(_debugLevel > 0, "   Previous time: " + temptime.ToString());
                    }
                }
                else
                {
                    Trace.WriteLineIf(_debugLevel > 0, "Unable to Seek Camera " + cameraNumber + " to " + time.ToString());
                }
            }
            else
            {
                Trace.WriteLineIf(_debugLevel > 0, "Unable to Seek Camera " + cameraNumber + " on Monitor " + monitorNumber);
            }
        }

        private void ChangePlaySpeed(int cameraNumber, int monitorNumber, int cellNumber, int speed)
        {
            CPPCli.DataSource camera = GetCamera(cameraNumber);
            CPPCli.Monitor monitor = GetMonitor(monitorNumber);
            if (camera != null && monitor != null)
            {
                if (monitor.MonitorCells.Count > cellNumber)
                {
                    CPPCli.MonitorCell cell = monitor.MonitorCells[cellNumber];
                    cell.Speed = speed;
                }
                else
                {
                    Trace.WriteLineIf(_debugLevel > 0, "Unable to change Camera " + cameraNumber + " speed to " + speed);
                }
            }
            else
            {
                Trace.WriteLineIf(_debugLevel > 0, "Unable to change Camera " + cameraNumber + " speed to " + speed + " on monitor " + monitorNumber);
            }
        }

        private void DisplayCameraOnMonitor(int cameraNumber, int monitorNumber, int cellNumber = 0)
        {
            CPPCli.DataSource camera = GetCamera(cameraNumber);
            CPPCli.Monitor monitor = GetMonitor(monitorNumber);
            if (camera != null && monitor != null)
            {
                if (monitor.MonitorCells.Count > cellNumber)
                {
                    CPPCli.MonitorCell cell = monitor.MonitorCells[cellNumber];
                    cell.DataSourceId = camera.Id;
                }
                else
                {
                    Trace.WriteLineIf(_debugLevel > 0, "Unable to display Camera " + cameraNumber + " on Monitor " + monitorNumber + " in cell " + cellNumber);
                }
            }
            else
            {
                Trace.WriteLineIf(_debugLevel > 0, "Unable to display Camera " + cameraNumber + " on Monitor " + monitorNumber);
            }
        }

        private CPPCli.DataSource GetCamera(int cameraNumber)
        {
            CPPCli.DataSource datasource = null;
            lock(_datasourceLock)
            {
                if (_datasources != null)
                {
                    try
                    {
                        datasource = _datasources.Find(x => (x.Number == cameraNumber));
                    }
                    catch
                    {
                        Trace.WriteLineIf(_debugLevel > 0, "Camera " + cameraNumber + " not found");
                    };
                }
            }
            return datasource;
        }

        private CPPCli.DataSource GetCamera(string id)
        {
            CPPCli.DataSource datasource = null;
            lock(_datasourceLock)
            {
                if (_datasources != null)
                {
                    try
                    {
                        datasource = _datasources.Find(x => (x.Id == id));
                    }
                    catch
                    {
                        Trace.WriteLineIf(_debugLevel > 0, "Camera " + id + " not found");
                    };
                }
            }
            return datasource;
        }

        private string GetCameraUUID(int cameraNumber)
        {
            string uuid = string.Empty;
            lock (_datasourceLock)
            {
                if (_datasources != null)
                {
                    try
                    {
                        CPPCli.DataSource dataSource = _datasources.Find(x => (x.Number == cameraNumber));
                        uuid = dataSource.Id;
                        Trace.WriteLineIf(_debugLevel > 0, "Camera " + cameraNumber + " found: " + uuid);
                    }
                    catch
                    {
                        Trace.WriteLineIf(_debugLevel > 0, "Camera " + cameraNumber + " not found");
                    };
                }
            }
            return uuid;
        }

        private CPPCli.Monitor GetMonitor(int monitorNumber)
        {
            CPPCli.Monitor monitor = null;
            lock(_monitorLock)
            {
                if (_monitors != null)
                {
                    try
                    {
                        monitor = _monitors.Find(x => x.Number == monitorNumber);
                    }
                    catch
                    {
                        Trace.WriteLineIf(_debugLevel > 0, "Monitor " + monitorNumber + " not found");
                    };
                }
            }
            return monitor;
        }

        private string GetMonitorUUID(int monitorNumber)
        {
            string uuid = string.Empty;
            lock(_monitorLock)
            {
                if (_monitors != null)
                {
                    try
                    {
                        CPPCli.Monitor monitor = _monitors.Find(x => x.Number == monitorNumber);
                        uuid = monitor.Id;
                        Trace.WriteLineIf(_debugLevel > 0, "Monitor " + monitorNumber + " found: " + uuid);
                    }
                    catch
                    {
                        Trace.WriteLineIf(_debugLevel > 0, "Monitor " + monitorNumber + " not found");
                    };
                }
            }
            return uuid;
        }

        private int GetCameraInCell(int monitorNumber, int cellNumber)
        {
            int cameraNumber = 0;
            CPPCli.Monitor monitor = GetMonitor(monitorNumber);
            if (monitor != null)
            {
                if (monitor.MonitorCells.Count > cellNumber)
                {
                    CPPCli.MonitorCell cell = monitor.MonitorCells[cellNumber];
                    CPPCli.DataSource dataSource = GetCamera(cell.DataSourceId);
                    if (dataSource != null)
                        cameraNumber = dataSource.Number;
                }
                else
                {
                    Trace.WriteLineIf(_debugLevel > 0, "GetCameraInCell has fewer cells than " + cellNumber);
                }
            }
            else
            {
                Trace.WriteLineIf(_debugLevel > 0, "GetCameraInCell cannot find monitor " + monitorNumber);
            }
            return cameraNumber;
        }
        #region PTZ Methods

        private CPPCli.PtzController GetPTZController(int cameraNumber)
        {
            CPPCli.PtzController ptzController = null;

            try
            {
                CPPCli.DataSource camera = GetCamera(cameraNumber);
                ptzController = camera.PTZController;
            }
            catch
            {
                Trace.WriteLineIf(_debugLevel > 0, "Unable to get PTZController for " + cameraNumber);
            }
            return ptzController;
        }

        private void SendPTZCommand(PTZInfo ptz)
        {
            try
            {
                CPPCli.PtzController ptzController = GetPTZController(_selectedCamera);
                if (ptzController != null)
                {
                    CPPCli.PtzController.ZoomDirections inOut;
                    if (ptz.zoom < 0)
                        inOut = CPPCli.PtzController.ZoomDirections.Out;
                    else if (ptz.zoom > 0)
                        inOut = CPPCli.PtzController.ZoomDirections.In;
                    else inOut = CPPCli.PtzController.ZoomDirections.Stop;
                    ptzController.ContinuousMove(ptz.pan, ptz.tilt, inOut);
                }
                else
                {
                    Trace.WriteLineIf(_debugLevel > 0, "No PTZController, Unable to PTZ");
                }
            }
            catch
            {
                Trace.WriteLineIf(_debugLevel > 0, "Exception, Unable to call PTZ");
            }
        }

        private void SendIrisCommand()
        {
            try
            {
                CPPCli.PtzController ptzController = GetPTZController(_selectedCamera);
                if (ptzController != null)
                {
                    CPPCli.PtzController.IrisDirections inOut;
                    if (_currentPTZInfo.iris < 0)
                        inOut = CPPCli.PtzController.IrisDirections.Close;
                    else if (_currentPTZInfo.iris > 0)
                        inOut = CPPCli.PtzController.IrisDirections.Open;
                    else inOut = CPPCli.PtzController.IrisDirections.Stop;
                    ptzController.ContinuousIris(inOut);
                }
                else
                {
                    Trace.WriteLineIf(_debugLevel > 0, "No PTZController, Unable to move Iris");
                }
            }
            catch
            {
                Trace.WriteLineIf(_debugLevel > 0, "Exception, Unable to move Iris");
            }
        }

        private void SendFocusCommand()
        {
            try
            {
                CPPCli.PtzController ptzController = GetPTZController(_selectedCamera);
                if (ptzController != null)
                {
                    CPPCli.PtzController.FocusDirections inOut;
                    if (_currentPTZInfo.focus < 0)
                        inOut = CPPCli.PtzController.FocusDirections.Near;
                    else if (_currentPTZInfo.focus > 0)
                        inOut = CPPCli.PtzController.FocusDirections.Far;
                    else inOut = CPPCli.PtzController.FocusDirections.Stop;
                    ptzController.ContinuousFocus(inOut);
                }
                else
                {
                    Trace.WriteLineIf(_debugLevel > 0, "No PTZController, Unable to move Focus");
                }
            }
            catch
            {
                Trace.WriteLineIf(_debugLevel > 0, "Exception, Unable to move Focus");
            }
        }

        private void SendPTZStop()
        {
            try
            {
                CPPCli.PtzController ptzController = GetPTZController(_selectedCamera);
                if (ptzController != null)
                {
                    ptzController.Stop();
                }
                else
                {
                    Trace.WriteLineIf(_debugLevel > 0, "No PTZController, Unable to Stop PTZ");
                }
            }
            catch
            {
                Trace.WriteLineIf(_debugLevel > 0, "Exception, Unable to stop PTZ");
            }
        }

        private int ASCIISpeedToVxSpeed(int ASCIIspeed)
        {
            int vxSpeed = ASCIIspeed;
            // todo: figure out translation of speed 1-64 to 1-???
            return vxSpeed;
        }

        private void SendGotoPreset(int presetNumber)
        {
            try
            {
                CPPCli.PtzController ptzController = GetPTZController(_selectedCamera);
                if (ptzController != null)
                {
                    List<CPPCli.Preset> presets = ptzController.GetPresets();
                    string presetString = "PRESET" + presetNumber.ToString();
                    CPPCli.Preset preset = presets.Find(x => x.Name == presetString);
                    if (preset != null)
                        ptzController.TriggerPreset(preset);
                }
                else
                {
                    Trace.WriteLineIf(_debugLevel > 0, "No PTZController, Unable to call Preset " + presetNumber);
                }
            }
            catch
            {
                Trace.WriteLineIf(_debugLevel > 0, "Exception, Unable to call Preset " + presetNumber);
            }
        }

        private void SendGotoPattern(int patternNumber)
        {
            try
            {
                CPPCli.PtzController ptzController = GetPTZController(_selectedCamera);
                if (ptzController != null)
                {
                    List<CPPCli.Pattern> patterns = ptzController.GetPatterns();
                    string patternString = "PATTERN" + patternNumber.ToString();
                    CPPCli.Pattern pattern = patterns.Find(x => x.Name == patternString);
                    if (pattern != null)
                        ptzController.TriggerPattern(pattern);
                }
                else
                {
                    Trace.WriteLineIf(_debugLevel > 0, "No PTZController, Unable to call Pattern " + patternNumber);
                }
            }
            catch
            {
                Trace.WriteLineIf(_debugLevel > 0, "Exception, Unable to call Pattern " + patternNumber);
            }
        }

        #endregion // PTZ Methods

        #region VxSDK communications
        private bool ConnectVxSystem(ref CPPCli.VXSystem vxSystem, string userName, string password, string ipAddress, int port, bool useSSL)
        {
            bool status = false;
            vxSystem = new CPPCli.VXSystem(ipAddress, port, useSSL);

            //var result = vxSystem.InitializeSdk(SdkKey);
            var result = CPPCli.VxGlobal.InitializeSdk(SdkKey);
            
            if (result != CPPCli.Results.Value.OK)
            {
                Trace.WriteLineIf(_debugLevel > 0, "Unable to connect to VideoXpert: SDK Key failed to initialize");
                status = false;
                vxSystem = null;
                return status;
            }

            result = vxSystem.Login(userName, password);
            if (result == CPPCli.Results.Value.OK)
            {
                Trace.WriteLineIf(_debugLevel > 0, "Logged into VideoXpert at " + ipAddress);
                status = true;
            }
            else
            {
                Trace.WriteLineIf(_debugLevel > 0, "Unable to log user " + userName + " into VideoXpert at " + ipAddress);
                status = false;
                vxSystem = null;
            }

            return status;
        }

        private bool ForwardEventToVx(CPPCli.NewEvent newEvent)
        {
            bool retVal = false;
            CPPCli.Situation situation = null;
            try
            {
                lock(_situationLock)
                {
                    situation = _situations.Find(x => x.Type == newEvent.SituationType);
                }
            }
            catch
            {
                Trace.WriteLineIf((_debugLevel > 0), "Situation " + newEvent.SituationType + " not found.");
            };

            if (situation != null)
            {
                Trace.WriteLineIf((_debugLevel > 0), "\nASCII Event match found: injecting Vx Event");
                Trace.WriteLineIf((_debugLevel > 0), "   GeneratorDeviceId: " + newEvent.GeneratorDeviceId);
                Trace.WriteLineIf((_debugLevel > 0), "   SourceDeviceId   : " + newEvent.SourceDeviceId);
                Trace.WriteLineIf((_debugLevel > 0), "   Situation        : " + newEvent.SituationType);
                if (_debugLevel > 0)
                {
                    foreach (var prop in newEvent.Properties)
                    {
                        Trace.WriteLine("   Property         : " + prop.Key + " , " + prop.Value);
                    }
                }

                CPPCli.Results.Value result = _vxSystem.InjectEvent(newEvent);
                if (result != CPPCli.Results.Value.OK)
                {
                    Trace.WriteLineIf((_debugLevel > 0), "ForwardEventToVx failed to inject event into Vx: " + result);
                }
                else retVal = true;
            }

            return retVal;
        }

        private bool SetLayout(int monitorNumber, CPPCli.Monitor.Layouts layout)
        {
            bool retVal = false;
            CPPCli.Monitor monitor = GetMonitor(monitorNumber);
            if (monitor != null)
            {
                monitor.Layout = layout;
                retVal = true;
            }
            return retVal;
        }
        #endregion

        #region POS ROUTINES
        private bool AtPOSDelimiter(string command)
        {
            bool found = false;
            if (command.EndsWith(_settings.POSSettings.EndDelimiter))
                found = true;
            return found;
        }

        private bool AtLineDelimiter(string command)
        {
            bool found = false;
            if (!string.IsNullOrEmpty(_settings.POSSettings.LineDelimiter))
            {
                if (command.EndsWith(_settings.POSSettings.LineDelimiter))
                    found = true;
            }
            return found;
        }

        private bool FindPOSKeyWord(string command)
        {
            bool found = false;
            if (command.Contains(_settings.POSSettings.KeyWord))
                found = true;
            return found;
        }

        // injects Alarm 1 as POS Event, alarm 1 with AlarmState 1 must be defined
        private void InjectPOSEvent(string command, bool receiptComplete)
        {
            //if (receiptComplete)
            //    Trace.Write("Complete: ");
            //Trace.WriteLine("InjectPOSEvent " + command);

            Alarm alarm = null;
            // Alarm 1 is used for reciept complete, 2 for line event
            int alarmNumber = 1;
            if (!receiptComplete)
                alarmNumber = 2;

            // bounds checked before it gets here
            try
            {
                alarm = _alarmConfig.Find(x => x.Number == alarmNumber);
            }
            catch
            {
                Trace.WriteLineIf(_debugLevel > 0, "Required Alarm number " + alarmNumber + " is missing from AlarmConfiguration.xml");
            }
            if (alarm != null)
            {
                vxSituation posSit = null;
                try
                {
                    List<vxSituation> sitList = alarm.Situations.ToList();
                    posSit = sitList.Find(x => x.AlarmState == 1); // AlarmState must be 1 (0 is not used for POS events)
                }
                catch
                {
                    Trace.WriteLineIf(_debugLevel > 0, "Required Alarm number " + alarmNumber + " is missing AlarmState of 1");
                }
                if (posSit != null)
                {
                    CPPCli.Situation vxSit = null;
                    try
                    {
                        //trigger alarm found in our alarm config xml
                        //now check if it's enableSit is in our known Vx situations
                        lock(_situationLock)
                        {
                            vxSit = _situations.Find(x => x.Type == posSit.Type);
                        }
                    }
                    catch
                    {
                        Trace.WriteLineIf(_debugLevel > 0, "Required Situation type " + posSit.Type + " is missing from Situation list (check CustomSituations.xml)");
                    }
                    // if Vx knows the situation type, inject the event
                    if (vxSit != null)
                    {
                        //found our enable sit in the Vx Situations.
                        Trace.WriteLineIf(_debugLevel > 0, "Found POS alarm sit type in Vx sitlist, " + vxSit.Type);
                        string sourceDeviceId;
                        if (string.IsNullOrEmpty(alarm.SourceDeviceId) || alarm.SourceDeviceId == "USE_INTEGRATION_ID")
                            sourceDeviceId = _settings.IntegrationId;
                        else
                            sourceDeviceId = alarm.SourceDeviceId;

                        while (command.Length > 0)
                        {
                            string posData;
                            if (command.Length > MAX_KVP_VALUE_SIZE)
                            {
                                posData = command.Substring(0, MAX_KVP_VALUE_SIZE - 1);
                                command = command.Substring(MAX_KVP_VALUE_SIZE);
                            }
                            else
                            {
                                posData = command;
                                command = string.Empty;
                            }
                            InjectPOSEvent(posSit, sourceDeviceId, posData, receiptComplete);
                        }
                    }
                    else
                    {
                        // situation not found.
                        Trace.WriteLineIf(_debugLevel > 0, "\nEnable Sit type, " + posSit.Type + ", not found in Vx sitlist");
                    }
                }
                else
                {
                    Trace.WriteLineIf(_debugLevel > 0, "Alarm enable situation not found in config xml for POS Alarm");
                }
            }
            else
            {
                Trace.WriteLineIf(_debugLevel > 0, "POS Alarm 1 not found in alarm cfg xml.");
            }
        }

        private void InjectPOSEvent(vxSituation situation, string sourceDeviceId, string posData, bool receiptComplete)
        {
            //found our enable sit in the Vx Situations.
            CPPCli.NewEvent newEvent = new CPPCli.NewEvent();
            newEvent.GeneratorDeviceId = _settings.IntegrationId;   // unique identifier for this integration
            newEvent.SourceDeviceId = sourceDeviceId;
            newEvent.SituationType = situation.Type;
            newEvent.Time = DateTime.UtcNow;

            List<KeyValuePair<string, string>> properties = new List<KeyValuePair<string, string>>();

            // add POS data to properties
            if (receiptComplete)
            {
                KeyValuePair<string, string> kvpPOS = new KeyValuePair<string, string>("pos_data_complete", posData);
                properties.Add(kvpPOS);
                Trace.WriteLineIf(_debugLevel > 0, "Inject pos_data_complete: " + posData);
            }
            else
            {
                KeyValuePair<string, string> kvpPOS = new KeyValuePair<string, string>("pos_data_line", posData);
                properties.Add(kvpPOS);
                Trace.WriteLineIf(_debugLevel > 0, "Inject pos_data_line: " + posData);
            }

            // add any other properties user wishes to add with event
            foreach (SituationProperty prop in situation.properties)
            {
                KeyValuePair<string, string> kvp = new KeyValuePair<string, string>(prop.Key, prop.Value);
                properties.Add(kvp);
            }

            if (properties.Count > 0)
                newEvent.Properties = properties;

            ForwardEventToVx(newEvent);
        }
#endregion
    }
}
