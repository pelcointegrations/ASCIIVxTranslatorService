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
    public class CustomSituations
    {
        [XmlElement("CustomSituation")]
        public CustomSituation[] customSituations;
    }

    [XmlRoot()]
    public class CustomSituation
    {
        private string situationType = string.Empty;
        private string name = string.Empty;
        private int severity = 1;
        private bool log = false;
        private bool notify = false;
        private bool displayBanner = false;
        private bool audible = false;
        private bool ackNeeded = false;
        private int autoAck = 0;

        public string SituationType
        {
            get { return situationType; }
            set { situationType = value; }
        }

        public string Name
        {
            get { return name; }
            set { name = value; }
        }

        public int Severity
        {
            get { return severity; }
            set { severity = value; }
        }

        public bool Log
        {
            get { return log; }
            set { log = value; }
        }

        public bool Notify
        {
            get { return notify; }
            set { notify = value; }
        }

        public bool DisplayBanner
        {
            get { return displayBanner; }
            set { displayBanner = value; }
        }

        public bool Audible
        {
            get { return audible; }
            set { audible = value; }
        }

        public bool AckNeeded
        {
            get { return ackNeeded; }
            set { ackNeeded = value; }
        }

        public int AutoAcknowledge
        {
            get { return autoAck; }
            set { autoAck = value; }
        }
    }
}
