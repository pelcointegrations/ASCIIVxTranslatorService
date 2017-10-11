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
    public class MonitorToCellMap
    {
        [XmlElement("MonitorToCell")]
        public MonitorToCell[] monitorToCellMap;
    }

    [XmlRoot()]
    public class MonitorToCell
    {
        private int _asciiMonitor;
        private int _vxMonitor;
        private int _vxCell;

        public int ASCIIMonitor
        {
            get { return _asciiMonitor; }
            set { _asciiMonitor = value; }
        }

        public int VxMonitor
        {
            get { return _vxMonitor; }
            set { _vxMonitor = value; }
        }

        public int VxCell
        {
            get { return _vxCell; }
            set { _vxCell = value; }
        }
    }
}
