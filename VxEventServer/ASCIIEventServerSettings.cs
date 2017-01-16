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
        private POSSettings posSettings;

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

        [XmlElement("POSSettings")]
        public POSSettings POSSettings
        {
            get { return posSettings; }
            set { posSettings = value; }
        }
    }

    public class EthernetSettings
    {
        private string address = string.Empty;
        private int port = 0;

        public string Address
        {
            get { return address; }
            set { address = value; }
        }

        public int Port
        {
            get { return port; }
            set { port = value; }
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

    public class POSSettings
    {
        private static int maxAllowedReceiptLength = 2048 * 10;
        private bool posMode = false;
        private int maxReceiptLength = 2048; // default max receipt size
        private string keyWord = string.Empty;
        private string endDelimiter = string.Empty;
        private string lineDelimiter = string.Empty;

        public bool POSMode
        {
            get { return posMode; }
            set { posMode = value; }
        }

        public int MaxReceiptLength
        {
            get { return maxReceiptLength; }
            set
            {
                if (value <= maxAllowedReceiptLength)
                    maxReceiptLength = value;
                else maxReceiptLength = maxAllowedReceiptLength;
            }
        }

        public string KeyWord
        {
            get { return keyWord; }
            set { keyWord = value; }
        }

        public string EndDelimiter
        {
            get { return endDelimiter; }
            set { endDelimiter = value; }
        }
        public string LineDelimiter
        {
            get { return lineDelimiter; }
            set { lineDelimiter = value; }
        }
    }
}
