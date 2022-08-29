﻿using FAnsi.Implementations.MicrosoftSQL;
using Microsoft.Data.SqlClient;
using NUnit.Framework;

namespace FAnsiTests.Server
{
    class ServerLevelUnitTests
    {
        [Test]
        public void ff()
        {
            var b = new SqlConnectionStringBuilder("Server=localhost;Database=RDMP_Catalogue;User ID=SA;Password=blah;Trust Server Certificate=true;");
            b.InitialCatalog = "master";

            Assert.AreEqual("Data Source=localhost;Initial Catalog=master;User ID=SA;Password=blah;Trust Server Certificate=True", b.ConnectionString);
        }
    }
}
