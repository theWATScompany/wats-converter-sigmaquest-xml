using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using Virinco.WATS.Interface;

namespace Virinco.WATS.Converter.SigmaQuest
{
    [TestClass]
    public class ConverterTests : TDM
    {
        [TestMethod]
        public void SetupClient()
        {
            SetupAPI(null, "location", "purpose", true);
            RegisterClient("Your WATS instance url", "username", "password/token");
            InitializeAPI(true);
        }

        [TestMethod]
        public void ConverterTest()
        {
            InitializeAPI(true);
            string fn = @"Data\test.xml";
            var converter = new GenericXMLConverter();
            using (FileStream file = new FileStream(fn, FileMode.Open))
            {
                SetConversionSource(new FileInfo(fn), converter.ConverterParameters, null);
                Report uut = converter.ImportReport(this, file);
                Submit(uut);
            }
        }
    }
}
