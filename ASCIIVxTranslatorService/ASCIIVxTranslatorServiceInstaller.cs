using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration.Install;
using System.Linq;


namespace ASCIIVxTranslatorService
{
    [RunInstaller(true)]
    public partial class ASCIIVxTranslatorServiceInstaller : Installer
    {
        public ASCIIVxTranslatorServiceInstaller()
        {
            InitializeComponent();
        }
    }
}
