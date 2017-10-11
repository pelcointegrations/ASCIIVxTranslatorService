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
        public int camera;
        public int pan;
        public int tilt;
        public int zoom;
        public int iris;
        public int focus;
        public int preset;
        public int pattern;
        public bool newPanCommand;
        public bool newTiltCommand;
        public bool newZoomCommand;
        public bool newIrisCommand;
        public bool newFocusCommand;
        public bool newPresetCommand;
        public bool newPatternCommand;

        public PTZInfo()
        {
            Clear();
        }

        public PTZInfo(PTZInfo ptz)
        {
            camera = ptz.camera;
            pan = ptz.pan;
            tilt = ptz.tilt;
            zoom = ptz.zoom;
            iris = ptz.iris;
            focus = ptz.focus;
            preset = ptz.preset;
            pattern = ptz.pattern;
            newPanCommand = ptz.newPanCommand;
            newTiltCommand = ptz.newTiltCommand;
            newZoomCommand = ptz.newZoomCommand;
            newIrisCommand = ptz.newIrisCommand;
            newFocusCommand = ptz.newFocusCommand;
            newPresetCommand = ptz.newPresetCommand;
            newPatternCommand = ptz.newPatternCommand;
        }

        public void Clear()
        {
            camera = 0;
            pan = 0;
            tilt = 0;
            zoom = 0;
            iris = 0;
            focus = 0;
            preset = 0;
            pattern = 0;
            newPanCommand = false;
            newTiltCommand = false;
            newZoomCommand = false;
            newIrisCommand = false;
            newFocusCommand = false;
            newPresetCommand = false;
            newPatternCommand = false;
        }

        public bool InProgress()
        {
            if (newPanCommand || newTiltCommand || newZoomCommand || newIrisCommand || newFocusCommand || newPresetCommand || newPatternCommand)
                return true;
            else return false;
        }

        public bool StoppingPTZ()
        {
            if (pan == 0 && tilt == 0 && zoom == 0 && iris == 0 && focus == 0)
                return true;
            else return false;
        }

        public void Merge(PTZInfo mergeInfo)
        {
            if (mergeInfo.newPanCommand)
            {
                this.pan = mergeInfo.pan;
                this.newPanCommand = true;
            }
            if (mergeInfo.newTiltCommand)
            {
                this.tilt = mergeInfo.tilt;
                this.newTiltCommand = true;
            }
            if (mergeInfo.newZoomCommand)
            {
                this.zoom = mergeInfo.zoom;
                this.newZoomCommand = true;
            }
            if (mergeInfo.newIrisCommand)
            {
                this.iris = mergeInfo.iris;
                this.newIrisCommand = true;
            }
            if (mergeInfo.newFocusCommand)
            {
                this.focus = mergeInfo.focus;
                this.newFocusCommand = true;
            }
            if (mergeInfo.newPresetCommand)
            {
                int camera = this.camera;
                Clear();
                this.camera = camera;
                this.preset = mergeInfo.preset;
                this.newPresetCommand = true;
            }
            if (mergeInfo.newPatternCommand)
            {
                int camera = this.camera;
                Clear();
                this.camera = camera;
                this.pattern = mergeInfo.pattern;
                this.newPatternCommand = true;
            }

        }
    }

    class Session
    {
        public string name = string.Empty;
        public string id = Guid.NewGuid().ToString();
        public int selectedCamera = 0;
        public int selectedMonitor = 0;
        public int selectedCell = 0;
        public PTZInfo ptzInfo = new PTZInfo();
        public object ptzLock = new object();
        public List<PTZInfo> ptzHoldList = new List<PTZInfo>();

        public Session(string sessionName)
        {
            name = sessionName;
        }
    }

    class ASCIIEventHandler
    {
        //private static string SdkKey = "C6B57D9440C8DFE461574971CE6A4811EF9CA6D254452E386D2748A464FD5606";
        private static int MAX_ASCII_COMMAND_LENGTH = 2048; // throw out commands bigger than this

        private CPPCli.VXSystem _vxSystem = null;
        private object _vxSystemLock = new object();

        private string _systemID = string.Empty;
        private List<CPPCli.Monitor> _monitors = null;
        private List<CPPCli.DataSource> _datasources = null;
        private List<CPPCli.Situation> _situations = null; //total situation list read from Vx
        private List<CustomSituation> _customSituations = null;
        private ASCIIEventServerSettings _settings = null;
        private List<Command> _commands = null;
        private List<string> _delimiters = null;
        private List<Alarm> _alarmConfig = null;
        private List<Script> _scripts = null;
        private List<MonitorToCell> _monitorToCellMapList = null;
        private string _ackResponse = string.Empty;
        private string _nackResponse = string.Empty;

        private DateTime _commandDelayUntilTime = DateTime.Now;

        private int _debugLevel;

        private Thread _listenASCIISerialThread = null;
        private Thread _listenASCIIEthernetThread = null;
        private Thread _listenASCIIEthernetThreadTCP = null;
        private Thread _processPTZCommandsThread = null;
        private Thread _refreshDataThread = null;
        private volatile bool _stopping = false;

        // each listener or handler thread now has its own session
        private List<Session> _sessions = new List<Session>();
        private object _sessionLock = new object();
        private Session testSession = new Session("ConsoleSession"); // for commands coming through console
        private Session tcpSingleSession = new Session("TCPSession"); // for commands coming through tcp handled through single session
        private bool isTCPMultiSession = false;

        public ASCIIEventHandler(CustomSituations customSits, 
            ASCIIEventServerSettings settings, 
            ASCIICommandConfiguration asciiCommands, 
            AlarmConfiguration alarmConfiguration,
            ASCIIScripts asciiScripts,
            MonitorToCellMap monitorToCellMap)
        {
            try
            {
                _settings = settings;
                _debugLevel = _settings.DebugLevel;

                //moved before InitializeVxWrapper so custom events from xml can be added to vx
                if ((customSits != null)&&(customSits.customSituations != null))
                    _customSituations = customSits.customSituations.ToList();

                if ((asciiScripts != null)&&(asciiScripts.scripts != null))
                    _scripts = asciiScripts.scripts.ToList();

                if (monitorToCellMap != null)
                    _monitorToCellMapList = monitorToCellMap.monitorToCellMap.ToList();

                // initialize _vxSystem
                Initialize();

                if ((asciiCommands != null) && (asciiCommands.Commands != null))
                {
                    _commands = asciiCommands.Commands.Command.ToList();
                    _delimiters = asciiCommands.Commands.GetDelimiters();
                    if (asciiCommands.Response != null)
                    {
                        if (! string.IsNullOrEmpty(asciiCommands.Response.Ack))
                            _ackResponse = asciiCommands.Response.Ack;
                        if (! string.IsNullOrEmpty(asciiCommands.Response.Nack))
                            _nackResponse = asciiCommands.Response.Nack;
                    }
                }

                if (alarmConfiguration != null)
                    _alarmConfig = alarmConfiguration.alarms.ToList();

                _stopping = false;

                _sessions.Add(testSession);
                _sessions.Add(tcpSingleSession);

                if ((_settings.SerialPortSettings != null) && (_settings.SerialPortSettings.PortName != string.Empty))
                {
                    this._listenASCIISerialThread = new Thread(this.ListenASCIISerialThread);
                    this._listenASCIISerialThread.Start();
                }
                if ((_settings.EthernetSettings != null) && (!string.IsNullOrEmpty(_settings.EthernetSettings.Port)))
                {
                    // 06/21/2017 add TCP listener as option
                    if (_settings.EthernetSettings.ConnectionType.ToUpper().Contains("TCP"))
                    {
                        if (_settings.EthernetSettings.ConnectionType.ToUpper().Contains("MULTI"))
                        {
                            isTCPMultiSession = true;
                        }

                        this._listenASCIIEthernetThreadTCP = new Thread(this.ListenASCIIEthernetThreadTCP);
                        this._listenASCIIEthernetThreadTCP.Start();
                    }
                    else
                    {
                        this._listenASCIIEthernetThread = new Thread(this.ListenASCIIEthernetThread);
                        this._listenASCIIEthernetThread.Start();
                    }
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

            // if TCP sessions have not ended, give at least 100 ms for them to detect
            // stopping and exit
            _sessions.Remove(testSession);
            _sessions.Remove(tcpSingleSession);

            if (_sessions.Count > 0)
            {
                Thread.Sleep(1000);
            }
        }

        #region INITIALIZATION OF VXSDK
        private CPPCli.VXSystem GetVxSystem()
        {
            // re-initialize connection if needed
            if ((_vxSystem == null))// || (!_vxCore.IsConnected()))
            {
                ForceReconnect();
            }
            return _vxSystem;
        }

        private void ForceReconnect()
        {
            lock (_vxSystemLock)
            {
                if (_vxSystem != null)
                {
                    _vxSystem.Dispose();
                    _vxSystem = null;
                }
                _monitors = null;
                _situations = null;
                _datasources = null;
                ConnectVxSystem(ref _vxSystem, _settings.VxUsername, _settings.VxPassword, _settings.VxCoreAddress, _settings.VxCorePort, true);
            }
        }

        private void Initialize()
        {
            lock(_vxSystemLock)
            {
                CPPCli.VXSystem system = GetVxSystem();
                if (system == null)
                {
                    Trace.WriteLine("Failed to connect to VideoXpert system at " + _settings.VxCoreAddress);
                }
                else
                {
                    _monitors = system.GetMonitors();
                    _datasources = system.GetDataSources();
                    _situations = system.GetSituations();

                    RegisterExternalDevice();

                    LoadCustomSituations();
                }
            }
        }

        private void RegisterExternalDevice()
        {
            string deviceName = "ASCII Vx Translator Service";
            lock (_vxSystemLock)
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
                        asciiDevice.Name = deviceName;
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
                                    thisDevice = devices.Find(x => (x.Ip == localIp && x.Type == CPPCli.Device.Types.External && x.Name == deviceName));
                                }
                            }
                            catch { };
                            if (thisDevice != null)
                            {
                                _settings.IntegrationId = thisDevice.Id;
                                Trace.WriteLineIf(_debugLevel > 0, "Integration registered with Vx: " + thisDevice.Name + " : " + thisDevice.Id);
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
                        Trace.WriteLineIf(_debugLevel > 0, "Integration already registered with Vx: " + thisDevice.Name + " : " + thisDevice.Id);
                    }
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
            lock (_vxSystemLock)
            {
                CPPCli.VXSystem system = GetVxSystem();
                if (system == null)
                    return;

                bool situationsAddedOrModified = false;
                foreach (CustomSituation custSit in _customSituations)
                {
                    CPPCli.Situation vxSit = null;
                    try
                    {
                        vxSit = _situations.Find(x => x.Type == custSit.SituationType);
                    }
                    catch { };

                    if (vxSit == null)
                    {
                        if (AddSituation(custSit))
                        {
                            Trace.WriteLineIf(_debugLevel > 0, "Added custom situation: " + custSit.SituationType);
                            situationsAddedOrModified = true; //at least one was added.
                        }
                    }
                    else
                    {
                        bool modified = false;
                        //Custom Sit is already in the system... 
                        // see if our xml version differs and patch each difference
                        if (custSit.AckNeeded != vxSit.IsAckNeeded)
                        {
                            vxSit.IsAckNeeded = custSit.AckNeeded;
                            modified = true;
                        }
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
                        // VXINT-1123, SourceDeviceId - must delete and re-add if this
                        // changes since VxSituation SourceDeviceId is read only
                        if (! string.IsNullOrEmpty(custSit.SourceDeviceId))
                        {
                            if (custSit.SourceDeviceId == "USE_INTEGRATION_ID")
                            {
                                custSit.SourceDeviceId = _settings.IntegrationId;
                            }
                            if (custSit.SourceDeviceId != vxSit.SourceDeviceId)
                            {
                                system.DeleteSituation(vxSit);
                                AddSituation(custSit);
                                situationsAddedOrModified = true;
                            }
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
                    _situations = _vxSystem.GetSituations();
                }
            }
        }

        private bool AddSituation(CustomSituation custSit)
        {
            bool success = false;
            lock (_vxSystemLock)
            {
                CPPCli.VXSystem system = GetVxSystem();
                if (system == null)
                    return false;

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
                // VXINT-1123, add SourceDeviceId
                if (! string.IsNullOrEmpty(custSit.SourceDeviceId))
                {
                    if (custSit.SourceDeviceId == "USE_INTEGRATION_ID")
                        newSit.SourceDeviceId = _settings.IntegrationId;
                    else
                        newSit.SourceDeviceId = custSit.SourceDeviceId;
                }
                newSit.Type = custSit.SituationType;
                CPPCli.Results.Value addRes = system.AddSituation(newSit);
                if (addRes == CPPCli.Results.Value.OK)
                {
                    success = true;
                }
            }
            return success;
        }

        #endregion

        private void ListenASCIISerialThread()
        {
            SerialPort serialPort = null;

            string commandStr = string.Empty;
            Session serialSession = new Session("SerialSession");

            {
                lock(_sessionLock)
                _sessions.Add(serialSession);
            }

            while (!_stopping)
            {
                if (serialPort != null)
                {
                    try
                    {
                        // don't process or read chars until wait time is up
                        if (DateTime.Now > _commandDelayUntilTime)
                        {
                            int rawChar = serialPort.ReadChar();
                            //Trace.WriteIf(_debugLevel > 1, " " + rawChar.ToString("x") + " ");
                            char charRead = Convert.ToChar(rawChar);
                            commandStr += charRead;
                            Trace.WriteLineIf(_debugLevel > 2, "Serial receive : " + charRead);
                            if (FindCommandDelimiter(commandStr))
                            {
                                string response = string.Empty;

                                Trace.WriteLineIf(_debugLevel > 0, "Command Delimiter found, Processing Command: " + commandStr);
                                bool cmdFound = ProcessCommand(commandStr, out response, ref serialSession);
                                if (cmdFound)
                                {
                                    commandStr = string.Empty;
                                }

                                if (response != string.Empty)
                                {
                                    Trace.WriteLineIf(_debugLevel > 0, "ProcessCommand response: " + response);
                                    serialPort.Write(response);
                                }
                            }
                            
                            if (commandStr.Length > MAX_ASCII_COMMAND_LENGTH)
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

            {
                lock(_sessionLock)
                _sessions.Remove(serialSession);
            }
        }

        // This thread has been modified for handling sessions as of version 2.0.5.0.  
        // The idea here is to overwrite previous commands with the most recent one.
        // For example, if a preset command comes in, a pan left should be overwritten
        // This speeds execution but causes several potential issues:
        // 1.  If the intention is to go to a preset then pan left, the preset may not make
        //     it all the way to its destination if the pan left comes in too fast.  This should be 
        //     acceptable as long as we do not delay the preset command getting to the device.
        // 2.  It is possible for the camera number to change if commands for different cameras
        //     come in before this thread executes.  This would be a loss of a ptz command.
        // Note also that scripts are handled in place and could possibly interfere with these commands
        private void ProcessPTZCommandThread()
        {
            while (!_stopping)
            {
                PTZInfo ptz = null;
                bool ptzCommand = false;
                bool irisCommand = false;
                bool focusCommand = false;
                bool patternCommand = false;
                bool presetCommand = false;
                Thread.Sleep(5);
                // look for first session with PTZ command and send
                // do not stay here and process each session, only do
                // one per loop.  Theoretically this could starve a session
                // but the likelihood is low that multiple sessions will
                // be active simultaneously to the point where starvation occurs
                foreach(Session session in _sessions)
                {
                    lock (session.ptzLock)
                    {
                        // if no ptz flags set
                        if (! session.ptzInfo.InProgress())
                        {
                            if (session.ptzHoldList.Count > 0)
                            {
                                session.ptzInfo = session.ptzHoldList.FirstOrDefault();
                                if (session.ptzInfo != null)
                                {
                                    session.ptzHoldList.Remove(session.ptzInfo);
                                }
                                //else session.ptzInfo = new PTZInfo();
                            }
                        }

                        if (session.ptzInfo.newPanCommand)
                        {
                            // clone ptz info to send
                            ptz = new PTZInfo(session.ptzInfo);
                            session.ptzInfo.newPanCommand = false;
                            ptzCommand = true;
                        }
                        if (session.ptzInfo.newTiltCommand)
                        {
                            // clone ptz info to send
                            if (ptz == null)
                                ptz = new PTZInfo(session.ptzInfo);
                            session.ptzInfo.newTiltCommand = false;
                            ptzCommand = true;
                        }
                        if (session.ptzInfo.newZoomCommand)
                        {
                            // clone ptz info to send
                            if (ptz == null)
                                ptz = new PTZInfo(session.ptzInfo);
                            session.ptzInfo.newZoomCommand = false;
                            ptzCommand = true;
                        }
                        if (session.ptzInfo.newIrisCommand)
                        {
                            // clone ptz info to send
                            if (ptz == null)
                                ptz = new PTZInfo(session.ptzInfo);
                            session.ptzInfo.newIrisCommand = false;
                            irisCommand = true;
                        }
                        if (session.ptzInfo.newFocusCommand)
                        {
                            // clone ptz info to send
                            if (ptz == null)
                                ptz = new PTZInfo(session.ptzInfo);
                            session.ptzInfo.newFocusCommand = false;
                            focusCommand = true;
                        }
                        if (session.ptzInfo.newPatternCommand)
                        {
                            // clone ptz info to send
                            if (ptz == null)
                                ptz = new PTZInfo(session.ptzInfo);
                            session.ptzInfo.newPatternCommand = false;
                            patternCommand = true;
                        }
                        if (session.ptzInfo.newPresetCommand)
                        {
                            // clone ptz info to send
                            if (ptz == null)
                                ptz = new PTZInfo(session.ptzInfo);
                            session.ptzInfo.newPresetCommand = false;
                            presetCommand = true;
                        }

                        if (ptz != null)
                            break;
                    }
                }

                // do this out of session lock
                if (ptz != null)
                {
                    // preset and pattern commands override ptz, focus or iris but we
                    // may have ptz, focus or iris come in after a preset or pattern
                    // so we execute the preset or pattern first, then perform
                    // the other ptz functions if they are present
                    if (presetCommand || patternCommand)
                    {
                        if (patternCommand)
                        {
                            SendGotoPattern(ptz.camera, ptz.pattern);
                        }
                        if (presetCommand)
                        {
                            SendGotoPreset(ptz.camera, ptz.preset);
                        }
                    }
                    
                    // This will stop a preset or pattern.  The only way these
                    // bits are set is if one of these commands came in after
                    // the preset or pattern command, so stopping them is
                    // appropriate
                    if (ptzCommand || irisCommand || focusCommand)
                    {
                        if (ptz.StoppingPTZ())
                            SendPTZStop(ptz);
                        else
                        {
                            if (ptzCommand)
                            {
                                SendPTZCommand(ptz);
                            }
                            if (irisCommand)
                            {
                                SendIrisCommand(ptz);
                            }
                            if (focusCommand)
                            {
                                SendFocusCommand(ptz);
                            }
                        }
                    }
                }
            }
        }

        private void RefreshDataThread()
        {
            int sleepInterval = 100;
            int intervalMonitorUpdate = 3 * 60 ; // 3 minutes intervals
            int intervalDataSourceUpdate = 4 * 60; // 4 minutes intervals
            int intervalSituationUpdate = 30 * 60; // 30 minutes interval (should not need to do this)
            int intervalSystemCheck = 2 * 60; // 2 minutes intervals

            TimeSpan monitorTimeSpan = new TimeSpan(0, 0, intervalMonitorUpdate);
            TimeSpan dataSourceTimeSpan = new TimeSpan(0, 0, intervalDataSourceUpdate);
            TimeSpan situationTimeSpan = new TimeSpan(0, 0, intervalSituationUpdate);
            TimeSpan systemCheckTimeSpan = new TimeSpan(0, 0, intervalSystemCheck);

            DateTime nextMonitorUpdate = DateTime.Now + monitorTimeSpan;
            DateTime nextDataSourceUpdate = DateTime.Now + dataSourceTimeSpan;
            DateTime nextSituationUpdate = DateTime.Now + situationTimeSpan;
            DateTime nextSystemCheck = DateTime.Now + systemCheckTimeSpan;

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
                        lock (_vxSystemLock)
                        {
                            CPPCli.VXSystem system = GetVxSystem();
                            if (system != null)
                            {
                                monitors = system.GetMonitors();
                            }
                        }
                    }
                    catch { };

                    if (monitors != null)
                    {
                        DateTime timeUpdate = DateTime.Now;
                        Trace.WriteLineIf((_debugLevel > 0), timeUpdate.ToString() + " Update Monitors " + monitors.Count);
                        lock (_vxSystemLock)
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
                        lock (_vxSystemLock)
                        {
                            CPPCli.VXSystem system = GetVxSystem();
                            lock (_vxSystemLock)
                            {
                                datasources = system.GetDataSources(); ;
                            }
                        }
                    }
                    catch { };

                    if (datasources != null)
                    {
                        DateTime timeUpdate = DateTime.Now;
                        Trace.WriteLineIf((_debugLevel > 0), timeUpdate.ToString() + " Update DataSources " + datasources.Count);
                        lock (_vxSystemLock)
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
                        lock (_vxSystemLock)
                        {
                            CPPCli.VXSystem system = GetVxSystem();
                            if (system != null)
                            {
                                situations = system.GetSituations();
                            }
                        }
                    }
                    catch { };

                    if (situations != null)
                    {
                        DateTime timeUpdate = DateTime.Now;
                        Trace.WriteLineIf((_debugLevel > 0), timeUpdate.ToString() + " Update Situations " + situations.Count);
                        lock (_vxSystemLock)
                        {
                            _situations = situations;
                        }
                    }
                    nextSituationUpdate = DateTime.Now + situationTimeSpan;
                }

                // Force reconnection?
                if (now > nextSystemCheck)
                {
                    lock (_vxSystemLock)
                    {
                        if (((_monitors == null) || (_monitors.Count == 0)) ||
                         ((_datasources == null) || (_datasources.Count == 0)) ||
                         ((_situations == null) || (_situations.Count == 0)))
                        {
                            Trace.WriteLineIf((_debugLevel > 0), "Forcing reconnect to VideoXpert");
                            ForceReconnect();
                            nextMonitorUpdate = DateTime.Now;
                            nextDataSourceUpdate = DateTime.Now;
                            nextSituationUpdate = DateTime.Now;
                        }
                    }

                    nextSystemCheck = DateTime.Now + systemCheckTimeSpan;
                }
            }
        }

        private void ListenASCIIEthernetThread()
        {
            Session udpSession = new Session("UDPSession");
            {
                lock(_sessionLock)
                _sessions.Add(udpSession);
            }

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
                        Trace.WriteLineIf(_debugLevel > 2, "Ethernet receive : " + receivedStr);
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

                            cmdFound = ProcessCommand(command, out response, ref udpSession);

                            if (response != string.Empty)
                            {
                                // send response back
                                Trace.WriteLineIf(_debugLevel > 0, "Command Response: " + response);
                                //Byte[] sendBytes = Encoding.UTF8.GetBytes (response);
                                //listener.Send(sendBytes, sendBytes.Length);
                            }

                            // if last command was wait command, kick out of while
                            if (_commandDelayUntilTime > DateTime.Now)
                                break;
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

            {
                lock(_sessionLock)
                _sessions.Remove(udpSession);
            }
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
                int port = Convert.ToInt32(_settings.EthernetSettings.Port);
                IPEndPoint endPoint = new IPEndPoint(address, port);
                UdpClient listener = new UdpClient(endPoint);
                listener.Client.ReceiveTimeout = 500; // 500 ms
                Trace.WriteLineIf((_debugLevel > 0), "Listening for UDP at address: " + _settings.EthernetSettings.Address + " port: " + _settings.EthernetSettings.Port);
                return listener;
            }
            catch (Exception e)
            {
                Trace.WriteLineIf((_debugLevel > 0), "Failed to open UDP port " + _settings.EthernetSettings.Port + " Exception: " + e.Message);
                return null; 
            }
        }

        private void ListenASCIIEthernetThreadTCP()
        {
            Socket listener = null;

            string commandStr = string.Empty;
            while (!_stopping)
            {
                if (listener != null)
                {
                    try
                    {
                        Socket handlerSocket = listener.Accept();
                        if (handlerSocket != null)
                        {
                            Thread clientThread = new Thread(new ParameterizedThreadStart(HandleTCPClient));
                            clientThread.IsBackground = true;
                            clientThread.Start(handlerSocket);
                        }
                        else
                        {
                            Thread.Sleep(5);   // very short sleep to release cpu
                        }
                    }
                    catch (Exception e)
                    {
                        Trace.WriteLineIf((_debugLevel > 0), "TCP Listener exception: " + e.Message);
                    }
                }
                else // listener is null
                {
                    listener = OpenTCPListener();
                    Thread.Sleep(3000);
                }
            }

            if (listener != null)
                listener.Close();
        }

        private Socket OpenTCPListener()
        {
            try
            {
                IPAddress address = IPAddress.Any;
                if (_settings.EthernetSettings.Address != string.Empty)
                {
                    address = IPAddress.Parse(_settings.EthernetSettings.Address);
                }
                int port = Convert.ToInt32(_settings.EthernetSettings.Port);
                IPEndPoint endPoint = new IPEndPoint(address, port);
                Socket listenSocket = new Socket(AddressFamily.InterNetwork, 
                                        SocketType.Stream,
                                        ProtocolType.Tcp);
                listenSocket.Bind(endPoint);
                listenSocket.Listen(100);
                Trace.WriteLineIf((_debugLevel > 0), "Listening for TCP at address: " + _settings.EthernetSettings.Address + " port: " + _settings.EthernetSettings.Port);

                return listenSocket;
            }
            catch (Exception e)
            {
                Trace.WriteLineIf((_debugLevel > 0), "Failed to open TCP port " + _settings.EthernetSettings.Port + " Exception: " + e.Message);
                return null; 
            }
        }

        private void HandleTCPClient(object clientSocket)
        {
            Session tcpSession;
            if (isTCPMultiSession)
            {
                tcpSession = new Session("TCPSession");
                {
                    lock(_sessionLock)
                    _sessions.Add(tcpSession);
                }

                Trace.WriteLineIf((_debugLevel > 1), "New " + tcpSession.name + " " + tcpSession.id);
            }
            else tcpSession = tcpSingleSession;

            string commandStr = string.Empty;
            NetworkStream stream = null;
            TcpClient tcpClient = new TcpClient();
            try
            {
                tcpClient.Client = (Socket)clientSocket;
                if (tcpClient != null)
                {
                    stream = tcpClient.GetStream();
                }

                if (stream != null)
                {
                    byte[] cmdBuffer = new byte[MAX_ASCII_COMMAND_LENGTH];

                    // Set a 100 millisecond timeout for reading.
                    stream.ReadTimeout = 100;
                    bool connected = true;
                    while((! _stopping) && connected)
                    {
                        int bytesRead = 0;
                        try
                        {
                            bytesRead = stream.Read(cmdBuffer, 0, cmdBuffer.Length);
                        }
                        catch (Exception e)
                        {
                            if (e is SocketException)
                            {
                                SocketException sExcept = (SocketException)e;
                                if (sExcept.ErrorCode != 10060) // time out
                                {
                                    Trace.WriteLineIf((_debugLevel > 1), "TCP Handler Socket exception: " + e.Message);
                                    connected = false;
                                }                             
                            }
                            else if (e is IOException)
                            {
                                IOException sExcept = (IOException)e;
                                if ((sExcept.InnerException != null)&&(sExcept.InnerException is SocketException))
                                {
                                    SocketException innerExcept = (SocketException)sExcept.InnerException;
                                    if (innerExcept.ErrorCode != 10060) // time out
                                    {
                                        Trace.WriteLineIf((_debugLevel > 1), "TCP Handler Socket exception: " + e.Message);
                                        connected = false;
                                    }                             
                                }
                            }
                            else
                            {
                                Trace.WriteLineIf((_debugLevel > 0), "TCP Handler Exception: " + e.Message);
                                connected = false;
                            }
                        }

                        if (tcpClient.Client.Poll(0, SelectMode.SelectRead))
                        {
                            byte[] checkConn = new byte[1];
                            if (tcpClient.Client.Receive(checkConn, SocketFlags.Peek) == 0)
                            {
                                connected = false;
                            }
                        }

                        if ((connected)&&(bytesRead > 0))
                        {
                            string receivedStr = Encoding.ASCII.GetString(cmdBuffer, 0, bytesRead);
                            for (int i = 0; i < bytesRead; i++)
                                cmdBuffer[i] = 0;
                            commandStr += receivedStr;
                            Trace.WriteLineIf(_debugLevel > 2, "TCP Ethernet receive : " + receivedStr);

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
                                    cmdFound = ProcessCommand(command, out response, ref tcpSession);
                                    
                                    // 06/23/2017, for TCP connection we are going to follow UDI Matrix response format
                                    // which is “AcK” (successful), and “NacK” (unsuccessful).
                                    if (! string.IsNullOrEmpty(response))
                                    {
                                        Byte[] sendBytes = Encoding.UTF8.GetBytes (response);
                                        stream.Write (sendBytes, 0, sendBytes.Length);                   
                                    }
                                    else if (cmdFound)
                                    {
                                        Byte[] sendBytes = Encoding.UTF8.GetBytes ("AcK");
                                        stream.Write (sendBytes, 0, sendBytes.Length);
                                    }
                                    // if a full command was processed and not supported send back a NacK
                                    else //if (string.IsNullOrEmpty(remainder))
                                    {
                                        Byte[] sendBytes = Encoding.UTF8.GetBytes ("NacK");
                                        stream.Write (sendBytes, 0, sendBytes.Length);
                                    }

                                    // if last command was wait command, kick out of while
                                    if (_commandDelayUntilTime > DateTime.Now)
                                        break;
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
                    }
                }
            }
            catch (Exception e)
            {
                Trace.WriteLineIf((_debugLevel > 0), "TCP Handler Thread Exception: " + e.Message);
            }

            if (stream != null)
                stream.Close();
            if (tcpClient != null)
                tcpClient.Close();

            if (isTCPMultiSession)
            {
                lock(_sessionLock)
                _sessions.Remove(tcpSession);
            }
            Trace.WriteLineIf((_debugLevel > 1), tcpSession.name + "  " + tcpSession.id + " ended");
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
                        // don't process or read chars until wait time is up
                        if (DateTime.Now > _commandDelayUntilTime)
                        {
                            commandStr += ch;
                            if (FindCommandDelimiter(commandStr))
                            {
                                Trace.WriteLineIf(_debugLevel > 0, "Command Delimiter found, Processing Command: " + commandStr);
                                string response = string.Empty;
                                bool cmdFound = ProcessCommand(commandStr, out response, ref testSession);
                                commandStr = string.Empty;
                                if (response != string.Empty)
                                    Trace.WriteLineIf(_debugLevel > 0, "Command Response: " + response);
                            }
                        }
                        else
                        {
                            Trace.WriteLineIf(_debugLevel > 0, DateTime.Now.ToString() + " Wait command in effect, until " + _commandDelayUntilTime.ToString());
                            Thread.Sleep(1000);
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
            if (testCommand.ToUpper().Contains("SCRIPT"))
            {
                int scriptNumber = 0;
                testCommand = testCommand.ToUpper().Replace("SCRIPT","");
                scriptNumber = Convert.ToInt32(testCommand);
                ExecuteScript(scriptNumber);
                return "Script Executed";
            }
            else
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
                        bool cmdFound = ProcessCommand(command, out response, ref testSession);
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
                lock(_vxSystemLock)
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
                lock (_vxSystemLock)
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

            lock (_vxSystemLock)
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

        private bool ProcessCommand(string commandStr, out string response, ref Session session)
        {
            bool cmdFound = false;
            response = string.Empty;

            // must be at least a command and delimiter
            if (commandStr.Length < 2)
            {
                Trace.WriteLineIf(_debugLevel > 0, "ProcessCommand: commandStr length < 2, exiting " + commandStr.Length);
                if (! string.IsNullOrEmpty(_nackResponse))
                {
                    response = _nackResponse;                    
                }
                return cmdFound;
            }
            Command command = new Command();
            string parameter = string.Empty;
            if (GetCommandFromString(commandStr, out command, out parameter))
            {
                response = HandleCommand(command, parameter, ref session);
                cmdFound = true;
            }
            else
            {
                // no command found
                if (! string.IsNullOrEmpty(_nackResponse))
                {
                    response = _nackResponse;                    
                }
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

        private string HandleCommand(Command command, string parameter, ref Session session)
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

            Trace.WriteIf(_debugLevel > 1, "Cam " + session.selectedCamera + " ");
            Trace.WriteLine("Received Command: " + command.Name + "  " + parameter);

            if ((command.Parameter != null)&&(command.Parameter.Type.ToLower().Contains("int")))
            {
                if ((parameter != "~") && (paramInt < command.Parameter.Min) || (paramInt > command.Parameter.Max))
                {
                    Trace.WriteLineIf(_debugLevel > 0, "Command " + command.Name + ": param " + paramInt.ToString() + " out of bounds");
                    if (! string.IsNullOrEmpty(_nackResponse))
                        response = _nackResponse;                    
                    return response;
                }
            }

            bool success = true;
            switch (command.Name)
            {
                case "KeepAlive":
                    break; // nothing to do, just let Ack happen
                case "SelectMonitor":
                    session.selectedMonitor = paramInt;
                    SelectMonitor(session);
                    break;
                case "SelectCell":
                    session.selectedCell = paramInt - 1;
                    SelectCell(session);
                    break;
                case "SelectCamera":
                    session.selectedCamera = paramInt;
                    SelectCamera(session);
                    break;
                case "NextCamera":
                    NextCamera(session);
                    break;
                case "PreviousCamera":
                    PreviousCamera(session);
                    break;
                case "SingleCameraMode":
                    session.selectedCell = 0;
                    SingleCameraMode(session);
                    break;
                case "CameraMode2x2":
                    session.selectedCell = paramInt - 1;
                    CameraMode2x2(session);
                    break;
                case "CameraMode3x3":
                    session.selectedCell = paramInt - 1;
                    CameraMode3x3(session);
                    break;
                case "CameraMode4x4":
                    session.selectedCell = paramInt - 1;
                    CameraMode4x4(session);
                    break;
                case "SetCameraLayout":
                    CPPCli.Monitor.Layouts layout = (CPPCli.Monitor.Layouts)(paramInt - 1); // 0 - 17, valid values
                    SetCameraLayout(session.selectedMonitor, layout);
                    break;
                case "Play":
                    Play(session);
                    break;
                case "Stop":
                    Stop(session);
                    break;
                case "Pause":
                    Pause(session);
                    break;
                case "FastForward":
                    FastForward(session, paramInt);
                    break;
                case "Rewind":
                    Rewind(session, paramInt);
                    break;
                case "Seek":
                    Seek(session, parameter);
                    break;
                case "ToggleLive":
                    ToggleLive(session);
                    break;
                case "PanLeft":
                    PanLeft(session, paramInt);
                    break;
                case "PanRight":
                    PanRight(session, paramInt);
                    break;
                case "TiltUp":
                    TiltUp(session, paramInt);
                    break;
                case "TiltDown":
                    TiltDown(session, paramInt);
                    break;
                case "Zoom":
                    Zoom(session, parameter);
                    break;
                case "Wide":
                    Wide(session, parameter);
                    break;
                case "StopPTZ":
                    StopPTZ(session);
                    break;
                case "ExecutePattern":
                    ExecutePattern(session, paramInt);
                    break;
                case "GotoPreset":
                    GotoPreset(session, paramInt);
                    break;
                case "FocusNear":
                    FocusNear(session, parameter);
                    break;
                case "FocusFar":
                    FocusFar(session, parameter);
                    break;
                case "IrisOpen":
                    IrisOpen(session, parameter);
                    break;
                case "IrisClose":
                    IrisClose(session, parameter);
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
                default:
                    // command not handled or understood
                    success = false;
                    break;
            }

            if (success)
            {
                if (! string.IsNullOrEmpty(_ackResponse))
                    response = _ackResponse;
            }
            else if (! string.IsNullOrEmpty(_nackResponse))
            {
                response = _nackResponse;                    
            }

            return response;
        }

        private void SelectMonitor(Session session)
        {
            session.selectedCell = 0;
            session.selectedCamera = GetCameraInCell(session.selectedMonitor, session.selectedCell);
        }

        private void SelectCell(Session session)
        {
            session.selectedCamera = GetCameraInCell(session.selectedMonitor, session.selectedCell);
        }

        private void SelectCamera(Session session)
        {
            try
            {
                DisplayCameraOnMonitor(session.selectedCamera, session.selectedMonitor, session.selectedCell);
            }
            catch (Exception e)
            {
                // todo: diagnose issue being seen here (comes from both Disconnect() and monCell.GoToLive
                // when camera number has already been selected - may need to debug in VxSDK
                Trace.WriteLineIf(_debugLevel > 3, "Exception in DisplayCameraOnMonitor " + e.Message);
            }
        }

        private void NextCamera(Session session)
        {
            if (CameraWithinBounds(session.selectedCamera + 1))
            { 
                session.selectedCamera++;
                DisplayCameraOnMonitor(session.selectedCamera, session.selectedMonitor, session.selectedCell);
            }
        }

        private void PreviousCamera(Session session)
        {
            if (CameraWithinBounds(session.selectedCamera - 1))
            {
                session.selectedCamera--;
                DisplayCameraOnMonitor(session.selectedCamera, session.selectedMonitor, session.selectedCell);
            }            
        }

        private void SingleCameraMode(Session session)
        {
            CPPCli.Monitor.Layouts layout = CPPCli.Monitor.Layouts.CellLayout1x1;
            SetCameraLayout(session.selectedMonitor, layout);
        }

        private void CameraMode2x2(Session session)
        {
            CPPCli.Monitor.Layouts layout = CPPCli.Monitor.Layouts.CellLayout2x2;
            SetCameraLayout(session.selectedMonitor, layout);
        }

        private void CameraMode3x3(Session session)
        {
            CPPCli.Monitor.Layouts layout = CPPCli.Monitor.Layouts.CellLayout3x3;
            SetCameraLayout(session.selectedMonitor, layout);
        }

        private void CameraMode4x4(Session session)
        {
            CPPCli.Monitor.Layouts layout = CPPCli.Monitor.Layouts.CellLayout4x4;
            SetCameraLayout(session.selectedMonitor, layout);
        }

        private void SetCameraLayout(int Monitor, CPPCli.Monitor.Layouts layout)
        {
            if (!SetLayout(Monitor, layout))
                Trace.WriteLineIf(_debugLevel > 0, "Failed to set Monitor " + Monitor + " to layout " + layout.ToString());
            else Trace.WriteLineIf(_debugLevel > 0, "Monitor " + Monitor + " layout set to " + layout.ToString());
        }

        private void Play(Session session)
        {
            ChangePlaySpeed(session.selectedCamera, session.selectedMonitor, session.selectedCell, 1);
        }

        private void Stop(Session session)
        {
            Disconnect(session.selectedMonitor, session.selectedCell);
        }

        private void Pause(Session session)
        {
            ChangePlaySpeed(session.selectedCamera, session.selectedMonitor, session.selectedCell, 0);
        }

        private void FastForward(Session session, int speed)
        {
            ChangePlaySpeed(session.selectedCamera, session.selectedMonitor, session.selectedCell, speed);
        }

        private void Rewind(Session session, int speed)
        {
            ChangePlaySpeed(session.selectedCamera, session.selectedMonitor, session.selectedCell, -speed);
        }

        private void Seek(Session session, string datetime)
        {
            try
            {
                DateTime time = DateTime.Parse(datetime);
                Seek(session.selectedCamera, session.selectedMonitor, session.selectedCell, time);
            }
            catch
            {
                Trace.WriteLineIf(_debugLevel > 0, "Seek failed - unable to parse time: " + datetime);
            }
            
        }

        private void ToggleLive(Session session)
        {
            GoToLive(session.selectedCamera, session.selectedMonitor, session.selectedCell);
        }

            // return true unless selected camera still needs to clear ptz info
            // and we want to ptz a different camera
        private bool PTZSessionOverWriteOK(Session session)
        {
            bool ok = true;
            if (session.selectedCamera != session.ptzInfo.camera)
            {
                return (! session.ptzInfo.InProgress());
            }
            return ok;
        }

        private void AddToPTZHoldList(Session session, PTZInfo ptzData)
        {
            bool updated = false;
            foreach(PTZInfo listPtzInfo in session.ptzHoldList)
            {
                if (listPtzInfo.camera == ptzData.camera)
                {
                    // merge new PTZ data into existing
                    listPtzInfo.Merge(ptzData);
                    updated = true;
                }
            }

            // not found in list so add to list
            if (! updated)
            {
                session.ptzHoldList.Add(ptzData);
            }
        }

        private void UpdatePTZSession(Session session, PTZInfo ptzData)
        {
            if (session.ptzInfo.camera == 0)
            {
                session.ptzInfo = ptzData;
            }
            else if (PTZSessionOverWriteOK(session))
            {
                if (session.ptzInfo.camera != ptzData.camera)
                    session.ptzInfo = ptzData;
                else
                    session.ptzInfo.Merge(ptzData);
            }
            else
            {
                AddToPTZHoldList(session, ptzData);
            }
        }

        private void PanLeft(Session session, int speed)
        {
            lock(session.ptzLock)
            {
                PTZInfo ptzData = new PTZInfo();
                ptzData.camera = session.selectedCamera;
                ptzData.pan = ASCIISpeedToVxSpeed(-speed);
                ptzData.newPanCommand = true;

                UpdatePTZSession(session, ptzData);
            }
        }

        private void PanRight(Session session, int speed)
        {
            lock(session.ptzLock)
            {
                PTZInfo ptzData = new PTZInfo();
                ptzData.camera = session.selectedCamera;
                ptzData.pan = ASCIISpeedToVxSpeed(speed);
                ptzData.newPanCommand = true;

                UpdatePTZSession(session, ptzData);
            }
        }

        private void TiltUp(Session session, int speed)
        {
            lock(session.ptzLock)
            {
                PTZInfo ptzData = new PTZInfo();
                ptzData.camera = session.selectedCamera;
                ptzData.tilt = ASCIISpeedToVxSpeed(speed);
                ptzData.newTiltCommand = true;

                UpdatePTZSession(session, ptzData);
            }
        }

        private void TiltDown(Session session, int speed)
        {
            lock(session.ptzLock)
            {
                PTZInfo ptzData = new PTZInfo();
                ptzData.camera = session.selectedCamera;
                ptzData.tilt = ASCIISpeedToVxSpeed(-speed);
                ptzData.newTiltCommand = true;

                UpdatePTZSession(session, ptzData);
            }
        }

        private void Zoom(Session session, string param)
        {
            lock(session.ptzLock)
            {
                PTZInfo ptzData = new PTZInfo();
                ptzData.camera = session.selectedCamera;
                if (param == "~")
                    ptzData.zoom = 0;
                else
                    ptzData.zoom = 1;
                ptzData.newZoomCommand = true;

                UpdatePTZSession(session, ptzData);
            }
        }

        private void Wide(Session session, string param)
        {
            lock(session.ptzLock)
            {
                PTZInfo ptzData = new PTZInfo();
                ptzData.camera = session.selectedCamera;
                if (param == "~")
                    ptzData.zoom = 0;
                else
                    ptzData.zoom = -1;
                ptzData.newZoomCommand = true;

                UpdatePTZSession(session, ptzData);
            }
        }

        private void StopPTZ(Session session)
        {
            lock(session.ptzLock)
            {
                PTZInfo ptzData = new PTZInfo();
                ptzData.camera = session.selectedCamera;
                // update all values to zero
                ptzData.newPanCommand = true;
                ptzData.newTiltCommand = true;
                ptzData.newZoomCommand = true;
                ptzData.newIrisCommand = true;
                ptzData.newFocusCommand = true;

                UpdatePTZSession(session, ptzData);
            }
        }

        private void ExecutePattern(Session session, int pattern)
        {
            //SendGotoPattern(session.selectedCamera, pattern);
            lock(session.ptzLock)
            {
                PTZInfo ptzData = new PTZInfo();
                ptzData.camera = session.selectedCamera;
                ptzData.pattern = pattern;
                ptzData.newPatternCommand = true;

                UpdatePTZSession(session, ptzData);
            }
        }

        private void GotoPreset(Session session, int preset)
        {
            //SendGotoPreset(session.selectedCamera, preset);
            lock(session.ptzLock)
            {
                PTZInfo ptzData = new PTZInfo();
                ptzData.camera = session.selectedCamera;
                ptzData.preset = preset;
                ptzData.newPresetCommand = true;

                UpdatePTZSession(session, ptzData);
            }
        }

        private void FocusNear(Session session, string param)
        {
            lock(session.ptzLock)
            {
                PTZInfo ptzData = new PTZInfo();
                ptzData.camera = session.selectedCamera;
                if (param == "~")
                    ptzData.focus = 0;
                else
                    ptzData.focus = -1;
                ptzData.newFocusCommand = true;

                UpdatePTZSession(session, ptzData);
            }
        }

        private void FocusFar(Session session, string param)
        {
            lock(session.ptzLock)
            {
                PTZInfo ptzData = new PTZInfo();
                ptzData.camera = session.selectedCamera;
                if (param == "~")
                    ptzData.focus = 0;
                else
                    ptzData.focus = 1;
                ptzData.newFocusCommand = true;

                UpdatePTZSession(session, ptzData);
            }
        }

        private void IrisOpen(Session session, string param)
        {
            lock(session.ptzLock)
            {
                PTZInfo ptzData = new PTZInfo();
                ptzData.camera = session.selectedCamera;
                if (param == "~")
                    ptzData.iris = 0;
                else
                    ptzData.iris = 1;
                ptzData.newIrisCommand = true;

                UpdatePTZSession(session, ptzData);
            }
        }

        private void IrisClose(Session session, string param)
        {
            lock(session.ptzLock)
            {
                PTZInfo ptzData = new PTZInfo();
                ptzData.camera = session.selectedCamera;
                if (param == "~")
                    ptzData.iris = 0;
                else
                    ptzData.iris = -1;
                ptzData.newIrisCommand = true;

                UpdatePTZSession(session, ptzData);
            }
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
                lock (_vxSystemLock)
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
                            vxSit = _situations.Find(x => x.Type == enableSit.Type);
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
                            foreach (SituationProperty prop in enableSit.Properties)
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

                        // now execute any scripts associated with setting the alarm situation
                        ExecuteScripts(enableSit);
                    }
                    else
                    {
                        Trace.WriteLineIf(_debugLevel > 0, "Alarm enable situation not found in config xml for Alarm " + alarmNumber);
                    }
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
                lock (_vxSystemLock)
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
                            vxSit = _situations.Find(x => x.Type == clearSit.Type);
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
                            foreach (SituationProperty prop in clearSit.Properties)
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

                        // now execute any scripts associated with clearing the alarm situation
                        ExecuteScripts(clearSit);
                    }
                    else
                    {
                        Trace.WriteLineIf(_debugLevel > 0, "Alarm clear situation not found in config xml for Alarm " + alarmNumber);
                    }
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

        private void GoToLive(int cameraNumber, int monitorNumber, int cell)
        {
            // can't set DateTime to null, so set it equal to DateTime.MinValue
            Seek(cameraNumber, monitorNumber, cell, new DateTime());
        }

        private void Disconnect(int monitorNumber, int cellNumber)
        {
            lock (_vxSystemLock)
            {
                int cell = cellNumber;
                // possibly overwrites cell if MonitorToCellMap in use
                CPPCli.Monitor monitor = GetMonitor(monitorNumber, ref cell);
                if (monitor != null)
                {
                    if (monitor.MonitorCells.Count > cell)
                    {
                        CPPCli.MonitorCell monCell = monitor.MonitorCells[cell];
                        monCell.Disconnect();
                    }
                }
            }
        }

        private void Seek(int cameraNumber, int monitorNumber, int cellNumber, DateTime time)
        {
            lock (_vxSystemLock)
            {
                CPPCli.DataSource camera = GetCamera(cameraNumber);
                int cell = cellNumber;
                // possibly overwrites cell if MonitorToCellMap in use
                CPPCli.Monitor monitor = GetMonitor(monitorNumber, ref cell);
                if (camera != null && monitor != null)
                {
                    if (monitor.MonitorCells.Count > cell)
                    {
                        CPPCli.MonitorCell monCell = monitor.MonitorCells[cell];
                        // DateTime.MinValue used to signal going to live
                        if (time == DateTime.MinValue)
                        {
                            monCell.GoToLive();
                            Trace.WriteLineIf(_debugLevel > 0, "Camera " + cameraNumber + " LIVE");
                        }
                        else
                        {
                            DateTime temptime = monCell.Time;
                            monCell.Time = time;
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
        }

        private void ChangePlaySpeed(int cameraNumber, int monitorNumber, int cellNumber, int speed)
        {
            lock (_vxSystemLock)
            {
                CPPCli.DataSource camera = GetCamera(cameraNumber);
                int cell = cellNumber;
                // possibly overwrites cell if MonitorToCellMap in use
                CPPCli.Monitor monitor = GetMonitor(monitorNumber, ref cell);
                if (camera != null && monitor != null)
                {
                    if (monitor.MonitorCells.Count > cell)
                    {
                        CPPCli.MonitorCell monCell = monitor.MonitorCells[cell];
                        monCell.Speed = speed;
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
        }

        private void DisplayCameraOnMonitor(int cameraNumber, int monitorNumber, int cellNumber = 0, int previousSeconds = 0)
        {
            // 6/21/2017 Camera number 0 not disconnects camera on selected monitor
            if (cameraNumber == 0)
            {
                Disconnect(monitorNumber, cellNumber);
                return;
            }
            lock (_vxSystemLock)
            {
                int cell = cellNumber;
                CPPCli.DataSource camera = GetCamera(cameraNumber);
                // possibly overwrites cell if MonitorToCellMap in use
                CPPCli.Monitor monitor = GetMonitor(monitorNumber, ref cell);
                Trace.WriteLineIf(_debugLevel > 0, "Display Camera " + cameraNumber + " on Monitor " + monitorNumber + " in cell " + (cell + 1));

                if (camera != null && monitor != null)
                {
                    if (monitor.MonitorCells.Count > cell)
                    {
                        CPPCli.MonitorCell monCell = monitor.MonitorCells[cell];
                        if (monCell.DataSourceId != camera.Id)
                            monCell.DataSourceId = camera.Id;
                        if (previousSeconds != 0)
                        {
                            DateTime utcTime = DateTime.UtcNow;
                            TimeSpan span = new TimeSpan(0, 0, previousSeconds);
                            Trace.WriteIf(_debugLevel > 0, "   UTC Time: " + utcTime.ToString());
                            utcTime = utcTime - span;
                            Trace.WriteLineIf(_debugLevel > 0, ", Setting time to " + utcTime.ToString());
                            monCell.Time = utcTime;
                            //utcTime = utcTime - span;
                            //Trace.WriteLineIf(_debugLevel > 0, ", Setting time to " + utcTime.ToString());
                            //monCell.Time = utcTime;
                            Trace.WriteLineIf(_debugLevel > 0, ", Setting time Complete");
                        }
                        else monCell.GoToLive();
                    }
                    else
                    {
                        Trace.WriteLineIf(_debugLevel > 0, "Unable to display Camera " + cameraNumber + " on Monitor " + monitorNumber + " in cell " + cell);
                    }
                }
                else
                {
                    Trace.WriteLineIf(_debugLevel > 0, "Unable to display Camera " + cameraNumber + " on Monitor " + monitorNumber);
                }
            }
        }

        private CPPCli.DataSource GetCamera(int cameraNumber)
        {
            CPPCli.DataSource datasource = null;
            lock (_vxSystemLock)
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
            lock (_vxSystemLock)
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
            lock (_vxSystemLock)
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

        private CPPCli.Monitor GetMonitor(int monitorNumber, ref int cell)
        {
            CPPCli.Monitor monitor = null;
            lock (_vxSystemLock)
            {
                if (_monitors != null)
                {
                    try
                    {
                        // if we have mapped ASCII monitors to cells, do the mapping
                        if (_monitorToCellMapList != null)
                        {
                            // if a mapping exists
                            if (_monitorToCellMapList.Exists(x => x.ASCIIMonitor == monitorNumber))
                            {
                                MonitorToCell mtoc = _monitorToCellMapList.Find(x => x.ASCIIMonitor == monitorNumber);
                                if (mtoc != null)
                                {
                                    monitorNumber = mtoc.VxMonitor;
                                    if (mtoc.VxCell > 0)
                                        cell = mtoc.VxCell - 1;
                                }
                            }
                        }

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
            lock (_vxSystemLock)
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
            lock (_vxSystemLock)
            {
                int cell = cellNumber;
                // possibly overwrites cell if MonitorToCellMap in use
                CPPCli.Monitor monitor = GetMonitor(monitorNumber, ref cell);
                if (monitor != null)
                {
                    if (monitor.MonitorCells.Count > cell)
                    {
                        CPPCli.MonitorCell monCell = monitor.MonitorCells[cell];
                        CPPCli.DataSource dataSource = GetCamera(monCell.DataSourceId);
                        if (dataSource != null)
                            cameraNumber = dataSource.Number;
                    }
                    else
                    {
                        Trace.WriteLineIf(_debugLevel > 0, "GetCameraInCell has fewer cells than " + cell);
                    }
                }
                else
                {
                    Trace.WriteLineIf(_debugLevel > 0, "GetCameraInCell cannot find monitor " + monitorNumber);
                }
            }

            return cameraNumber;
        }
        #region PTZ Methods

        private CPPCli.PtzController GetPTZController(int cameraNumber)
        {
            CPPCli.PtzController ptzController = null;
            lock (_vxSystemLock)
            {
                try
                {
                    CPPCli.DataSource camera = GetCamera(cameraNumber);
                    ptzController = camera.PTZController;
                }
                catch
                {
                    Trace.WriteLineIf(_debugLevel > 0, "Unable to get PTZController for " + cameraNumber);
                }
            }
            return ptzController;
        }

        private void SendPTZCommand(PTZInfo ptz)
        {
            try
            {
                lock (_vxSystemLock)
                {
                    CPPCli.PtzController ptzController = GetPTZController(ptz.camera);
                    if (ptzController != null)
                    {
                        Trace.WriteLineIf(_debugLevel > 1, "Cam " + ptz.camera + " PTZ pan: " + ptz.pan + " tilt: " + ptz.tilt + " zoom: " + ptz.zoom);
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
            }
            catch
            {
                Trace.WriteLineIf(_debugLevel > 0, "Exception, Unable to call PTZ");
            }
        }

        private void SendIrisCommand(PTZInfo ptz)
        {
            try
            {
                lock (_vxSystemLock)
                {
                    CPPCli.PtzController ptzController = GetPTZController(ptz.camera);
                    if (ptzController != null)
                    {
                        Trace.WriteLineIf(_debugLevel > 1, "Cam " + ptz.camera + " iris: " + ptz.iris);
                        CPPCli.PtzController.IrisDirections inOut;
                        if (ptz.iris < 0)
                            inOut = CPPCli.PtzController.IrisDirections.Close;
                        else if (ptz.iris > 0)
                            inOut = CPPCli.PtzController.IrisDirections.Open;
                        else inOut = CPPCli.PtzController.IrisDirections.Stop;
                        ptzController.ContinuousIris(inOut);
                    }
                    else
                    {
                        Trace.WriteLineIf(_debugLevel > 0, "No PTZController, Unable to move Iris");
                    }
                }
            }
            catch
            {
                Trace.WriteLineIf(_debugLevel > 0, "Exception, Unable to move Iris");
            }
        }

        private void SendFocusCommand(PTZInfo ptz)
        {
            try
            {
                lock (_vxSystemLock)
                {
                    CPPCli.PtzController ptzController = GetPTZController(ptz.camera);
                    if (ptzController != null)
                    {
                        Trace.WriteLineIf(_debugLevel > 1, "Cam " + ptz.camera + " focus: " + ptz.focus);
                        CPPCli.PtzController.FocusDirections inOut;
                        if (ptz.focus < 0)
                            inOut = CPPCli.PtzController.FocusDirections.Near;
                        else if (ptz.focus > 0)
                            inOut = CPPCli.PtzController.FocusDirections.Far;
                        else inOut = CPPCli.PtzController.FocusDirections.Stop;
                        ptzController.ContinuousFocus(inOut);
                    }
                    else
                    {
                        Trace.WriteLineIf(_debugLevel > 0, "No PTZController, Unable to move Focus");
                    }
                }
            }
            catch
            {
                Trace.WriteLineIf(_debugLevel > 0, "Exception, Unable to move Focus");
            }
        }

        private void SendPTZStop(PTZInfo ptz)
        {
            try
            {
                lock (_vxSystemLock)
                {
                    CPPCli.PtzController ptzController = GetPTZController(ptz.camera);
                    if (ptzController != null)
                    {
                        Trace.WriteLineIf(_debugLevel > 1, "Cam " + ptz.camera + " PTZ STOP");
                        ptzController.Stop();
                    }
                    else
                    {
                        Trace.WriteLineIf(_debugLevel > 0, "No PTZController, Unable to Stop PTZ");
                    }
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

        private void SendGotoPreset(int camera, int presetNumber)
        {
            try
            {
                lock (_vxSystemLock)
                {
                    CPPCli.PtzController ptzController = GetPTZController(camera);
                    if (ptzController != null)
                    {
                        Trace.WriteLineIf(_debugLevel > 1, "Cam " + camera + " Call Preset: " + presetNumber);
                        List<CPPCli.Preset> presets = ptzController.GetPresets();
                        string presetString = "PRESET" + presetNumber.ToString();
                        CPPCli.Preset preset = presets.Find(x => x.Name.ToUpper() == presetString.ToUpper());
                        if (preset != null)
                            ptzController.TriggerPreset(preset);
                        else Trace.WriteLineIf(_debugLevel > 1, "Preset " + presetNumber + " NOT FOUND");
                    }
                    else
                    {
                        Trace.WriteLineIf(_debugLevel > 0, "No PTZController, Unable to call Preset " + presetNumber);
                    }
                }
            }
            catch
            {
                Trace.WriteLineIf(_debugLevel > 0, "Exception, Unable to call Preset " + presetNumber);
            }
        }

        private void SendGotoPattern(int camera, int patternNumber)
        {
            try
            {
                lock (_vxSystemLock)
                {
                    CPPCli.PtzController ptzController = GetPTZController(camera);
                    if (ptzController != null)
                    {
                        Trace.WriteLineIf(_debugLevel > 1, "Cam " + camera + " Call Pattern: " + patternNumber);
                        List<CPPCli.Pattern> patterns = ptzController.GetPatterns();
                        string patternString = "PATTERN" + patternNumber.ToString();
                        CPPCli.Pattern pattern = patterns.Find(x => x.Name == patternString);
                        if (pattern != null)
                            ptzController.TriggerPattern(pattern);
                        else Trace.WriteLineIf(_debugLevel > 1, "Pattern " + patternNumber + " NOT FOUND");
                    }
                    else
                    {
                        Trace.WriteLineIf(_debugLevel > 0, "No PTZController, Unable to call Pattern " + patternNumber);
                    }
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
            //var result = CPPCli.VxGlobal.InitializeSdk(SdkKey);
            
            //if (result != CPPCli.Results.Value.OK)
            //{
            //    Trace.WriteLineIf(_debugLevel > 0, "Unable to connect to VideoXpert: SDK Key failed to initialize");
            //    status = false;
            //    vxSystem = null;
            //    return status;
            //}

            var result = vxSystem.Login(userName, password);
            if (result == CPPCli.Results.Value.OK)
            {
                Trace.WriteLineIf(_debugLevel > 0, "Logged into VideoXpert at " + ipAddress);
                status = true;
            }
            else
            {
                Trace.WriteLineIf(_debugLevel > 0, "Unable to log user " + userName + " into VideoXpert at " + ipAddress);
                Trace.WriteLineIf(_debugLevel > 2, "   Password " + password);
                Trace.WriteLineIf(_debugLevel > 2, "   result " + result);
                status = false;
                vxSystem = null;
            }

            return status;
        }

        private bool ForwardEventToVx(CPPCli.NewEvent newEvent)
        {
            bool retVal = false;
            CPPCli.Situation situation = null;
            lock (_vxSystemLock)
            {
                try
                {
                    situation = _situations.Find(x => x.Type == newEvent.SituationType);
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

                    CPPCli.Results.Value result = CPPCli.Results.Value.UnknownError;
                    lock (_vxSystemLock)
                    {
                        CPPCli.VXSystem system = GetVxSystem();
                        if (system != null)
                            result = system.InjectEvent(newEvent);
                    }
                    if (result != CPPCli.Results.Value.OK)
                    {
                        Trace.WriteLineIf((_debugLevel > 0), "ForwardEventToVx failed to inject event into Vx: " + result);
                    }
                    else retVal = true;
                }
            }

            return retVal;
        }

        private bool SetLayout(int monitorNumber, CPPCli.Monitor.Layouts layout)
        {
            bool retVal = false;
            lock (_vxSystemLock)
            {
                int cell = 0;
                CPPCli.Monitor monitor = GetMonitor(monitorNumber, ref cell);
                if (monitor != null)
                {
                    monitor.Layout = layout;
                    retVal = true;
                }
            }
            return retVal;
        }
        #endregion

        #region Script Action Handling
        private void ExecuteScripts(vxSituation vxSit)
        {
            if ((vxSit != null) && (vxSit.ExecuteScripts != null))
            {
                foreach (int scriptNumber in vxSit.ExecuteScripts)
                {
                    ExecuteScript(scriptNumber);
                }
            }
        }

        void ExecuteScript(int scriptNumber)
        {
            try
            {
                Script script = _scripts.Find(x => x.Number == scriptNumber);
                if (script != null)
                {
                    foreach (Action action in script.Actions)
                    {
                        switch (action.Name.ToLower())
                        {
                            case "setlayout":
                            {
                                int mon = Convert.ToInt32(action.Monitor);
                                CPPCli.Monitor.Layouts layout = StringToMonitorLayout(action.Layout);
                                Trace.WriteLineIf((_debugLevel > 0), "Script: SetLayout " + action.Layout + " On Monitor " + mon);
                                SetLayout(mon, layout);
                                Trace.WriteLineIf((_debugLevel > 0), "Script: SetLayout Complete");
                                break;
                            }
                            case "displaycamera":
                            {
                                int mon = Convert.ToInt32(action.Monitor);
                                int cell = Convert.ToInt32(action.Cell);
                                if (cell > 0) // cell is 0 based, so subtract 1
                                    cell--;
                                int camera = Convert.ToInt32(action.Camera);
                                int previousSeconds = 0;
                                if (!string.IsNullOrEmpty(action.PreviousSeconds))
                                    previousSeconds = Convert.ToInt32(action.PreviousSeconds);
                                Trace.WriteLineIf((_debugLevel > 0), "Script: DisplayCamera " + camera + " On Monitor " + mon + " in Cell " + (cell + 1));
                                DisplayCameraOnMonitor(camera, mon, cell, previousSeconds);
                                Trace.WriteLineIf((_debugLevel > 0), "Script: DisplayCamera Complete");
                                break;
                            }
                            case "disconnectcamera":
                            {
                                int mon = Convert.ToInt32(action.Monitor);
                                int cell = Convert.ToInt32(action.Cell);
                                if (cell > 0) // cell is 0 based, so subtract 1
                                    cell--;
                                Trace.WriteLineIf((_debugLevel > 0), "Script: Disconnect Camera from Monitor " + mon + " in Cell " + (cell + 1));
                                Disconnect(mon, cell);
                                Trace.WriteLineIf((_debugLevel > 0), "Script: Disconnect Complete");
                                break;
                            }
                            case "gotopreset":
                            {
                                int camera = Convert.ToInt32(action.Camera);
                                Trace.WriteLineIf((_debugLevel > 0), "Script: Camera " + camera + " GotoPreset " + action.Preset);
                                SendGotoPreset(camera, action.Preset);
                                Trace.WriteLineIf((_debugLevel > 0), "Script: GotoPreset Complete");
                                break;
                            }
                            case "runpattern":
                            {
                                int camera = Convert.ToInt32(action.Camera);
                                Trace.WriteLineIf((_debugLevel > 0), "Script: Camera " + camera + " RunPattern " + action.Pattern);
                                SendGotoPattern(camera, action.Pattern);
                                Trace.WriteLineIf((_debugLevel > 0), "Script: RunPattern Complete");
                                break;
                            }
                            case "bookmark":
                            {
                                int camera = Convert.ToInt32(action.Camera);
                                string description = action.Description;
                                if (string.IsNullOrEmpty(description))
                                {
                                    description = "Script " + scriptNumber + " BookMark";
                                }
                                Trace.WriteLineIf((_debugLevel > 0), "Script: BookMark " + camera + " Description " + description);
                                CreateBookmark(camera, description);
                                Trace.WriteLineIf((_debugLevel > 0), "Script: BookMark Complete");
                                break;
                            }
                            default:
                                break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Trace.WriteLine("Exception in ExecuteScript " + scriptNumber + ":" + e.Message);
            }
        }

        private void SendGotoPreset(int camera, string presetStr)
        {
            try
            {
                lock (_vxSystemLock)
                {
                    CPPCli.PtzController ptzController = GetPTZController(camera);
                    if (ptzController != null)
                    {
                        List<CPPCli.Preset> presets = ptzController.GetPresets();
                        CPPCli.Preset preset = presets.Find(x => x.Name.ToUpper() == presetStr.ToUpper());
                        if (preset != null)
                            ptzController.TriggerPreset(preset);
                        else
                        {
                            Trace.WriteLineIf((_debugLevel > 0), "Unable to find preset " + presetStr);
                            Trace.WriteLineIf((_debugLevel > 0), "Available:");
                            foreach (CPPCli.Preset prst in presets)
                            {
                                Trace.WriteLineIf((_debugLevel > 0), "   " + prst.Name);
                            }
                        }
                    }
                    else
                    {
                        Trace.WriteLineIf(_debugLevel > 0, "No PTZController for camera " + camera + " Unable to call Preset " + presetStr);
                    }
                }
            }
            catch
            {
                Trace.WriteLineIf(_debugLevel > 0, "Exception, Unable to call Preset " + presetStr + " on camera " + camera);
            }
        }

        private void SendGotoPattern(int camera, string patternStr)
        {
            try
            {
                lock (_vxSystemLock)
                {
                    CPPCli.PtzController ptzController = GetPTZController(camera);
                    if (ptzController != null)
                    {
                        List<CPPCli.Pattern> patterns = ptzController.GetPatterns();
                        CPPCli.Pattern pattern = patterns.Find(x => x.Name == patternStr);
                        if (pattern != null)
                            ptzController.TriggerPattern(pattern);
                    }
                    else
                    {
                        Trace.WriteLineIf(_debugLevel > 0, "No PTZController, Unable to call Pattern " + patternStr);
                    }
                }
            }
            catch
            {
                Trace.WriteLineIf(_debugLevel > 0, "Exception, Unable to call Pattern " + patternStr);
            }
        }

        private void CreateBookmark(int camera, string description)
        {
            try
            {
                lock (_vxSystemLock)
                {
                    CPPCli.VXSystem system = GetVxSystem();
                    if (system != null)
                    {
                        CPPCli.DataSource dataSource = GetCamera(camera);
                        DateTime time = DateTime.Now;
                        var newBookmark = new CPPCli.NewBookmark
                        {
                            Description = description,
                            Time = time.ToUniversalTime(),
                            DataSourceId = dataSource.Id
                        };

                        var result = system.CreateBookmark(newBookmark);
                        if (result != CPPCli.Results.Value.OK)
                        {
                            Trace.WriteLineIf(_debugLevel > 0, "Unable to create BookMark for camera " + camera + " Result: " + result);
                        }
                    }

                }
            }
            catch (Exception e)
            {
                Trace.WriteLineIf(_debugLevel > 0, "Unable to create BookMark for camera " + camera + " Exception: " + e.Message);
            }
        }

        private CPPCli.Monitor.Layouts StringToMonitorLayout(string layoutStr)
        {
            switch (layoutStr.ToLower())
            {
                case "1x1":
                    return CPPCli.Monitor.Layouts.CellLayout1x1;
                case "1x2":
                    return CPPCli.Monitor.Layouts.CellLayout1x2;
                case "2x1":
                    return CPPCli.Monitor.Layouts.CellLayout2x1;
                case "2x2":
                    return CPPCli.Monitor.Layouts.CellLayout2x2;
                case "2x3":
                    return CPPCli.Monitor.Layouts.CellLayout2x3;
                case "3x2":
                    return CPPCli.Monitor.Layouts.CellLayout3x2;
                case "3x3":
                    return CPPCli.Monitor.Layouts.CellLayout3x3;
                case "4x3":
                    return CPPCli.Monitor.Layouts.CellLayout4x3;
                case "4x4":
                    return CPPCli.Monitor.Layouts.CellLayout4x4;
                case "1+12":
                    return CPPCli.Monitor.Layouts.CellLayout1plus12;
                case "2+8":
                    return CPPCli.Monitor.Layouts.CellLayout2plus8;
                case "3+4":
                    return CPPCli.Monitor.Layouts.CellLayout3plus4;
                case "1+5":
                    return CPPCli.Monitor.Layouts.CellLayout1plus5;
                case "1+7":
                    return CPPCli.Monitor.Layouts.CellLayout1plus7;
                case "12+1":
                    return CPPCli.Monitor.Layouts.CellLayout12plus1;
                case "8+2":
                    return CPPCli.Monitor.Layouts.CellLayout8plus2;
                case "1+4 (tall)":
                    return CPPCli.Monitor.Layouts.CellLayout1plus4tall;
                case "1+4 (wide)":
                    return CPPCli.Monitor.Layouts.CellLayout1plus4wide;
                default:
                    return CPPCli.Monitor.Layouts.CellLayout2x2;
            }
        }
        #endregion
    }
}
