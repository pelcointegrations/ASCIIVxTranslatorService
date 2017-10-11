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
    public class ASCIIScripts
    {
        [XmlElement("Script")]
        public Script[] scripts;
    }

    [XmlRoot()]
    public class Script
    {
        private string _name;
        private int _number;

        public string Name
        {
            get { return _name; }
            set { _name = value; }
        }

        public int Number
        {
            get { return _number; }
            set { _number = value; }
        }

        [XmlElement("Action")]
        public Action[] Actions;
    }

    public class Action
    {
        private string name;
        private string monitor;
        private string cell;
        private string camera;
        private string preset;
        private string pattern;
        private string layout;
        private string previousSeconds;
        private string relay;
        private string state;
        private string deviceIp;
        private string description;

        public string Name
        {
            get { return name; }
            set { name = value; }
        }

        public string Monitor
        {
            get { return monitor; }
            set { monitor = value; }
        }

        public string Cell
        {
            get { return cell; }
            set { cell = value; }
        }

        public string Camera
        {
            get { return camera; }
            set { camera = value; }
        }

        public string Preset
        {
            get { return preset; }
            set { preset = value; }
        }

        public string Pattern
        {
            get { return pattern; }
            set { pattern = value; }
        }

        public string Layout
        {
            get { return layout; }
            set { layout = value; }
        }

        public string PreviousSeconds
        {
            get { return previousSeconds; }
            set { previousSeconds = value; }
        }

        public string Relay
        {
            get { return relay; }
            set { relay = value; }
        }

        public string State
        {
            get { return state; }
            set { state = value; }
        }

        public string DeviceIP
        {
            get { return deviceIp; }
            set { deviceIp = value; }
        }

        public string Description
        {
            get { return description; }
            set { description = value; }
        }
    }
}
