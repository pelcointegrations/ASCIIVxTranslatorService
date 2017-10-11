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
    public class ASCIICommandConfiguration
    {
        private Commands commands;
        private Response response;

        [XmlElement("Commands")]
        public Commands Commands
        {
            get { return commands; }
            set { commands = value; }
        }

        [XmlElement("Response")]
        public Response Response
        {
            get { return response; }
            set { response = value; }
        }
    }

    public class Commands
    {
        private Command[] command;
        private List<string> _delimiters = null;
        
        [XmlElement("Command")]
        public Command[] Command
        {
            get { return command; }
            set { command = value; }
        }

        public List<string> GetDelimiters()
        {
            if (_delimiters == null)
            {
                _delimiters = new List<string>();
                var cmds = Command.ToList();
                foreach(Command cmd in cmds)
                {
                    if (!_delimiters.Contains(cmd.Delimiter))
                        _delimiters.Add(cmd.Delimiter);
                }
            }
            return _delimiters;
        }
    }

    public class Command
    {
        private string name = string.Empty;
		private string _value = string.Empty;
        private string delimiter = string.Empty;
        private Parameter parameter;

		public string Name
        {
            get { return name; }
            set { name = value; }
        }

		public string Value
        {
            get { return _value; }
            set { _value = value; }
        }

        public string Delimiter
        {
            get { return delimiter; }
            set { delimiter = value; }
        }

        [XmlElement("Parameter")]
        public Parameter Parameter
        {
            get { return parameter; }
            set { parameter = value; }
        }
    }

    public class Parameter
    {
        private string type;
        private int min;
        private int max;
        private string position;

        public string Type
        {
            get { return type; }
            set { type = value; }
        }

        public int Min
        {
            get { return min; }
            set { min = value; }
        }

        public int Max
        {
            get { return max; }
            set { max = value; }
        }

        public string Position
        {
            get { return position; }
            set { position = value; }
        }
    }

    public class Response
    {
        private string ack = string.Empty;
        private string nack = string.Empty;

        public string Ack
        {
            get { return ack; }
            set { ack = value; }
        }

        public string Nack
        {
            get { return nack; }
            set { nack = value; }
        }
    }
}
