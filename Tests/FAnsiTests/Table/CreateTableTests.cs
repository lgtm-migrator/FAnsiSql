﻿using FAnsi;
using FAnsi.TypeTranslation;
using NUnit.Framework;

namespace FAnsiSqlTests.Table
{
    class CreateTableTests:DatabaseTests
    {
        [TestCase(DatabaseType.MySql)]
        [TestCase(DatabaseType.MicrosoftSQLServer)]
        [TestCase(DatabaseType.Oracle)]
        public void CreateSimpleTable_Exists(DatabaseType type)
        {
            var db = GetTestDatabase(type);
            var table = db.CreateTable("People", new[]
            {
                new DatabaseColumnRequest("Name", new DatabaseTypeRequest(typeof (string), 10))
            });

            Assert.IsTrue(table.Exists());

            table.Drop();

            Assert.IsFalse(table.Exists());
        }
    }
}
