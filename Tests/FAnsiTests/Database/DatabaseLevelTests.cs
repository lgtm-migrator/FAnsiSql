﻿using System.Data;
using FAnsi;
using FAnsi.Discovery;
using FAnsi.Implementation;
using NUnit.Framework;
using TypeGuesser;

namespace FAnsiTests.Database
{
    class DatabaseLevelTests : DatabaseTests
    {
        [TestCase(DatabaseType.MySql)]
        [TestCase(DatabaseType.MicrosoftSQLServer)]
        [TestCase(DatabaseType.Oracle)]
        public void Database_Exists(DatabaseType type)
        {
            var server = GetTestDatabase(type);
            Assert.IsTrue(server.Exists(), "Server " + server + " did not exist");
        }


        [TestCase(DatabaseType.MySql,false)]
        [TestCase(DatabaseType.MicrosoftSQLServer,false)]
        [TestCase(DatabaseType.Oracle,true)]
        public void Test_ExpectDatabase(DatabaseType type, bool upperCase)
        {
            var helper = ImplementationManager.GetImplementation(type).GetServerHelper();
            var server = new DiscoveredServer(helper.GetConnectionStringBuilder("loco","db",null,null));
            var db = server.ExpectDatabase("omg");
            Assert.AreEqual(upperCase?"OMG":"omg",db.GetRuntimeName());
        }

        [TestCase(DatabaseType.MySql)]
        [TestCase(DatabaseType.MicrosoftSQLServer)]
        [TestCase(DatabaseType.Oracle)]
        public void Test_CreateSchema(DatabaseType type)
        {
            var db = GetTestDatabase(type);

            Assert.DoesNotThrow(()=>db.CreateSchema("Frank"));

            if (type == DatabaseType.MicrosoftSQLServer)
            {
                var tbl = db.CreateTable("Heyyy",
                    new[] {new DatabaseColumnRequest("fff", new DatabaseTypeRequest(typeof(string), 10))},"Frank");

                Assert.IsTrue(tbl.Exists());

                if(type == DatabaseType.MicrosoftSQLServer)
                    Assert.AreEqual("Frank",tbl.Schema);
            }
        }

    }
}