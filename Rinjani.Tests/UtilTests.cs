using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rinjani;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace Rinjani.Tests
{
    [TestClass()]
    public class UtilTests
    {
        [TestMethod()]
        public void UnixTimeStampToDateTimeTest()
        {
            double dd = 1523775784985;
            DateTime dt = Util.UnixTimeStampToDateTime(dd);
        }
    }
}