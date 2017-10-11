using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace ASCIIEvents
{
    [XmlRoot()]
    public class ASCIIEventServerSettings
    {
        private int debugLevel = 0;
        private string vxCoreAddress = "10.221.213.232";
        private int vxCorePort = 443;
        private string vxUsername = "admin";
        private string vxPassword = "pel2899100";
        private string integrationId = "E024457E-B2A2-49B5-AE62-20816418650F"; // default IntegrationId
        private EthernetSettings ethernetSettings;
		private SerialPortSettings serialPortSettings;

        public int DebugLevel
        {
            get { return debugLevel; }
            set { debugLevel = value; }
        }

        public string VxCoreAddress
        {
            get { return vxCoreAddress; }
            set 
            { 
                if (value != string.Empty) 
                    vxCoreAddress = value; 
            }
        }

        public int VxCorePort
        {
            get { return vxCorePort; }
            set { vxCorePort = value; }
        }

        public string VxUsername
        {
            get { return vxUsername; }
            set
            {
                if (value != string.Empty)
                {
                    try
                    {
                        byte[] decodedBytes = Convert.FromBase64String(value);
                        vxUsername = Encoding.UTF8.GetString(decodedBytes);
                        vxUsername = vxUsername.Replace("\r", "");
                        vxUsername = vxUsername.Replace("\n", "");
                        vxUsername = vxUsername.Trim();
                    }
                    catch
                    {
                        System.Diagnostics.Trace.WriteLine("Exception: Unable to convert username from Base64, defaulted to " + vxUsername);
                    }
                }
            }
        }

        public string VxPassword
        {
            get { return vxPassword; }
            set
            {
                if (value != string.Empty)
                {
                    try
                    { 
                        byte[] decodedBytes = Convert.FromBase64String(value);
                        vxPassword = Encoding.UTF8.GetString(decodedBytes);
                        vxPassword = vxPassword.Replace("\r", "");
                        vxPassword = vxPassword.Replace("\n", "");
                        vxPassword = vxPassword.Trim();
                    }
                    catch
                    {
                        System.Diagnostics.Trace.WriteLine("Exception: Unable to convert password from Base64, defaulted to " + vxPassword);
                    }
                }
            }
        }
        public string IntegrationId
        {
            get { return integrationId; }
            set
            {
                if (value != string.Empty)
                    integrationId = value;
            }
        }

        [XmlElement("EthernetSettings")]
        public EthernetSettings EthernetSettings
        {
            get { return ethernetSettings; }
            set { ethernetSettings = value; }
        }

        [XmlElement("SerialPortSettings")]
		public SerialPortSettings SerialPortSettings
        {
            get { return serialPortSettings; }
            set { serialPortSettings = value; }
        }
    }

    public class EthernetSettings
    {
        private string address = string.Empty;
        private string port = string.Empty;
        private string connectionType = "UDP";

        public string Address
        {
            get { return address; }
            set { address = value; }
        }

        public string Port
        {
            get { return port; }
            set { port = value; }
        }

        public string ConnectionType
        {
            get { return connectionType; }
            set { connectionType = value; }
        }
    }

    public class SerialPortSettings
    {
        private string portName = string.Empty;
		private int baudRate = 0;
		private int dataBits = 0;
		private string parity = string.Empty;
        private string stopBits = string.Empty;
		
		public string PortName
        {
            get { return portName; }
            set { portName = value; }
        }

		public int BaudRate
        {
            get { return baudRate; }
            set { baudRate = value; }
        }
		
		public int DataBits
        {
            get { return dataBits; }
            set { dataBits = value; }
        }

		public string Parity
        {
            get { return parity; }
            set { parity = value; }
        }
		
		public string StopBits
        {
            get { return stopBits; }
            set { stopBits = value; }
        }		
    }
}
