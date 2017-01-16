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
    public class AlarmConfiguration
    {
        [XmlElement("Alarm")]
        public Alarm[] alarms;
    }

    [XmlRoot()]
    public class Alarm
    {
        private string name = string.Empty;
        private int number = 0;

        private string generatorId = string.Empty;
        private string sourceId = string.Empty;

        public string Name
        {
            get { return name; }
            set { name = value; }
        }

        public int Number
        {
            get { return number; }
            set { number = value; }
        }

        public string GeneratorDeviceId
        {
            get { return generatorId; }
            set { generatorId = value; }
        }

        public string SourceDeviceId
        {
            get { return sourceId; }
            set { sourceId = value; }
        }

        [XmlElement("Situation")]
        public vxSituation[] Situations;
    }

    [XmlRoot()]
    public class vxSituation
    {
        private int alarmState = 0;
        private string type = string.Empty;

        [XmlElement("Property")]
        public SituationProperty[] properties;

        public int AlarmState
        {
            get { return alarmState; }
            set { alarmState = value; }
        }

        public string Type
        {
            get { return type; }
            set { type = value; }
        }
    }

    public class SituationProperty
    {
        private string key;
        private string val;

        public string Key
        {
            get { return key; }
            set { key = value; }
        }

        public string Value
        {
            get { return val; }
            set { val = value; }
        }
    }
}
