using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Reflection;
using ASCIIEvents;

using System.Xml;
using System.Xml.Serialization;

namespace VxEventServer
{
    public class VxEventServerManager : IDisposable
    {
        private static string customSituationsFileName = "CustomSituations.xml";
        private static string asciiEventServerSettingsFileName = "ASCIIEventServerSettings.xml";
        //private static string defaultCustomSituationsFileName = "defaultCustomSituations.xml";
        private static string defaultAsciiEventServerSettingsFileName = "defaultASCIIEventServerSettings.xml";
        private static string asciiCommandConfigurationFileName = "ASCIICommandConfiguration.xml";
        private static string defaultAsciiCommandConfigurationFileName = "defaultASCIICommandConfiguration.xml";
        private static string alarmConfigurationFileName = "AlarmConfiguration.xml";
        private static string defaultAlarmConfigurationFileName = "defaultAlarmConfiguration.xml";
        private static string asciiScriptsFileName = "ASCIIScripts.xml";
        private static string monitorToCellMapFileName = "MonitorToCellMap.xml";

        private static CustomSituations _customSituations = null;
        private static ASCIIEventServerSettings _settings = null;
        private static ASCIICommandConfiguration _commands = null;
        private static AlarmConfiguration _alarmConfiguration = null;
        private static ASCIIScripts _asciiScripts = null;
        private static MonitorToCellMap _monitorToCellMap = null;

        public bool Initialized { get; private set; }
        private ASCIIEventHandler _ASCIIEventHandler = null;

        public static VxEventServerManager Instance
        {
            get
            {
                return InstanceClass.Instance;
            }
        }

        private class InstanceClass
        {
            public static readonly VxEventServerManager Instance = new VxEventServerManager();

            static InstanceClass()
            {
            }
        }

        /// <summary>
        /// Init the VxEventServerManager
        /// </summary>
        public VxEventServerManager Init()
        {
            if (this.Initialized)
                return this;

            Reload();

            _ASCIIEventHandler = new ASCIIEventHandler(_customSituations, _settings, _commands, _alarmConfiguration, _asciiScripts, _monitorToCellMap);

            this.Initialized = true;

            return this;
        }

        private static void Reload()
        {
            _settings = GetAsciiEventServerSettings(GetFullPath(asciiEventServerSettingsFileName));
            if (_settings == null)
                _settings = GetAsciiEventServerSettings(GetFullPath(defaultAsciiEventServerSettingsFileName));
            _customSituations = GetCustomSituationSettings(GetFullPath(customSituationsFileName));
            _commands = GetASCIICommands(GetFullPath(asciiCommandConfigurationFileName));
            if (_commands == null)
                _commands = GetASCIICommands(GetFullPath(defaultAsciiCommandConfigurationFileName));
            _alarmConfiguration = GetAlarmConfiguration(GetFullPath(alarmConfigurationFileName));
            if (_alarmConfiguration == null)
                _alarmConfiguration = GetAlarmConfiguration(GetFullPath(defaultAlarmConfigurationFileName));
            _asciiScripts = GetASCIIScripts(GetFullPath(asciiScriptsFileName));
            _monitorToCellMap = GetMonitorToCellMap(GetFullPath(monitorToCellMapFileName));
        }

        private static string GetFullPath(string filename)
        {
            string path = GetCurrentPath();
            path += "\\" + filename;
            return path;
        }

        private static string GetCurrentPath()
        {
            string path = string.Empty;
            var currentPath = new System.IO.DirectoryInfo(Assembly.GetExecutingAssembly().Location);
            path = System.IO.Path.GetDirectoryName(currentPath.FullName);
            return path;
        }

        private static CustomSituations GetCustomSituationSettings(string path)
        {
            CustomSituations situations = null;
            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(CustomSituations));
                Stream fs = File.OpenRead(path);

                situations = (CustomSituations)serializer.Deserialize(fs);
            }
            catch (Exception e)
            {
                Trace.WriteLineIf(((_settings != null)&&(_settings.DebugLevel > 0)), "Failed to load custom situations: " + e.Message);
            }
            return situations;
        }

        private static ASCIIEventServerSettings GetAsciiEventServerSettings(string path)
        {
            ASCIIEventServerSettings settings = null;
            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(ASCIIEventServerSettings));
                Stream fs = File.OpenRead(path);

                settings = (ASCIIEventServerSettings)serializer.Deserialize(fs);
            }
            catch (Exception e)
            {
                Trace.WriteLine("Failed to load ASCIIEventServerSettings: " + e.Message);
            }
            return settings;
        }

        private static ASCIICommandConfiguration GetASCIICommands(string path)
        {
            ASCIICommandConfiguration commands = null;
            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(ASCIICommandConfiguration));
                Stream fs = File.OpenRead(path);

                commands = (ASCIICommandConfiguration)serializer.Deserialize(fs);
            }
            catch (Exception e)
            {
                Trace.WriteLineIf(((_settings != null) && (_settings.DebugLevel > 0)), "Failed to load ASCII commands: " + e.Message);
            }
            return commands;
        }

        private static AlarmConfiguration GetAlarmConfiguration(string path)
        {
            AlarmConfiguration commands = null;
            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(AlarmConfiguration));
                Stream fs = File.OpenRead(path);

                commands = (AlarmConfiguration)serializer.Deserialize(fs);
            }
            catch (Exception e)
            {
                Trace.WriteLineIf(((_settings != null) && (_settings.DebugLevel > 0)), "Failed to load Alarm configuration: " + e.Message);
            }
            return commands;
        }

        private static ASCIIScripts GetASCIIScripts(string path)
        {
            ASCIIScripts scripts = null;
            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(ASCIIScripts));
                Stream fs = File.OpenRead(path);

                scripts = (ASCIIScripts)serializer.Deserialize(fs);
            }
            catch (Exception e)
            {
                Trace.WriteLineIf(((_settings != null) && (_settings.DebugLevel > 0)), "Failed to load ASCII Scripts: " + e.Message);
            }
            return scripts;
        }
        
        private static MonitorToCellMap GetMonitorToCellMap(string path)
        {
            MonitorToCellMap mtocMap = null;
            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(MonitorToCellMap));
                Stream fs = File.OpenRead(path);

                mtocMap = (MonitorToCellMap)serializer.Deserialize(fs);
            }
            catch (Exception e)
            {
                Trace.WriteLineIf(((_settings != null) && (_settings.DebugLevel > 2)), "Failed to load MonitorToCellMap : " + e.Message);
            }
            return mtocMap;
        }

        public void Dispose()
        {
        }

        /// <summary>
        /// Print Status of service
        /// </summary>
        public void PrintStatus()
        {
            Console.WriteLine("\nSTATUS:\n");

            if (_ASCIIEventHandler != null)
            {
                string ASCIIStatus = _ASCIIEventHandler.GetStatus();
                Console.WriteLine(ASCIIStatus);
            }
            else Console.WriteLine("ASCIIEventHandler is NULL");
        }

        /// <summary>
        /// Pass a Test String to command interpreter
        /// </summary>
        /// <param name="testString">String to be interpreted by ASCII parser</param>
        public string PassTestString(string testString)
        {
            string response = string.Empty;
            if (_ASCIIEventHandler != null)
            {
                testString = testString.Replace("\n", "");
                testString = testString.Replace("\r", "");
                response = _ASCIIEventHandler.ProcessTestCommand(testString);
            }
            return response;
        }

        /// <summary>
        /// Interpret Test File
        /// </summary>
        /// <param name="filename">Filename containing POS data to be interpreted</param>
        public void InterpretTestFile(string fileName)
        {
            if (_ASCIIEventHandler != null)
            {
                _ASCIIEventHandler.ProcessTestFile(fileName);
            }
        }

        /// <summary>
        /// List Data Sources
        /// </summary>
        /// <param name="partialCameraName">Filter for returned datasources.  May be empty.</param>
        /// <returns>First datasource matching containing partial camera name or all datasources</returns>
        public string ListDataSources(string partialCameraName)
        {
            string response = string.Empty;
            if (_ASCIIEventHandler != null)
            {
                partialCameraName = partialCameraName.Replace("\n", "");
                partialCameraName = partialCameraName.Replace("\r", "");
                response = _ASCIIEventHandler.GetDataSourceInfo(partialCameraName);
            }
            return response;
        }

        /// <summary>
        /// List Monitors
        /// </summary>
        /// <param name="partialMonitorName">Filter for returned monitors.  May be empty.</param>
        /// <returns>First monitor matching containing partial monitor name or all monitors</returns>
        public string ListMonitors(string partialMonitorName)
        {
            string response = string.Empty;
            if (_ASCIIEventHandler != null)
            {
                partialMonitorName = partialMonitorName.Replace("\n", "");
                partialMonitorName = partialMonitorName.Replace("\r", "");
                response = _ASCIIEventHandler.GetMonitorInfo(partialMonitorName);
            }
            return response;
        }

        /// <summary>
        /// List Situations
        /// </summary>
        /// <param name="partialSituation">Filter for returned situations.  May be empty.</param>
        /// <returns>First situaiton matching containing partial situation name or all situations</returns>
        public string ListSituations(string partialSituation)
        {
            string response = string.Empty;
            if (_ASCIIEventHandler != null)
            {
                string[] situationTypes = _ASCIIEventHandler.GetSituationTypes();
                if (situationTypes != null)
                {
                    foreach (string situationType in situationTypes)
                    {
                        if ((String.IsNullOrEmpty(partialSituation)) || (situationType.Contains(partialSituation)))
                        {
                            response += situationType + "\n";
                        }
                    }
                }
            }
            return response;
        }
    }
}
