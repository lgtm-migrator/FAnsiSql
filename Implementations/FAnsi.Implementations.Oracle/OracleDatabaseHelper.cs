﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using FAnsi.Discovery;
using FAnsi.Discovery.QuerySyntax;
using FAnsi.Discovery.TypeTranslation;
using Oracle.ManagedDataAccess.Client;

namespace FAnsi.Implementations.Oracle
{
    public class OracleDatabaseHelper : DiscoveredDatabaseHelper
    {
        public override IDiscoveredTableHelper GetTableHelper()
        {
            return new OracleTableHelper();
        }

        public override void DropDatabase(DiscoveredDatabase database)
        {
             using(var con = (OracleConnection)database.Server.GetConnection())
             {
                 con.Open();
                 var cmd = new OracleCommand("DROP USER \"" + database.GetRuntimeName() + "\" CASCADE ",con);
                 cmd.ExecuteNonQuery();
             }
        }

        public override Dictionary<string, string> DescribeDatabase(DbConnectionStringBuilder builder, string database)
        {
            throw new NotImplementedException();
        }

        protected override string GetCreateTableSqlLineForColumn(DatabaseColumnRequest col, string datatype, IQuerySyntaxHelper syntaxHelper)
        {
            if (col.IsAutoIncrement)
                return string.Format("{0} NUMBER {1}",col.ColumnName, syntaxHelper.GetAutoIncrementKeywordIfAny());

            return base.GetCreateTableSqlLineForColumn(col, datatype, syntaxHelper);
        }

        public override DirectoryInfo Detach(DiscoveredDatabase database)
        {
            throw new NotImplementedException();
        }

        public override void CreateBackup(DiscoveredDatabase discoveredDatabase, string backupName)
        {
            throw new NotImplementedException();
        }

        public override IEnumerable<DiscoveredTable> ListTables(DiscoveredDatabase parent, IQuerySyntaxHelper querySyntaxHelper, DbConnection connection, string database, bool includeViews, DbTransaction transaction = null)
        {
            List<DiscoveredTable> tables = new List<DiscoveredTable>();
            
            //find all the tables
            using(var cmd = new OracleCommand("SELECT table_name FROM all_tables where owner='" + database + "'", (OracleConnection) connection))
            {
                cmd.Transaction = transaction as OracleTransaction;

                var r = cmd.ExecuteReader();

                while (r.Read())
                    tables.Add(new DiscoveredTable(parent,r["table_name"].ToString(),querySyntaxHelper));
            }
            
            //find all the views
            if(includeViews)
                using(var cmd = new OracleCommand("SELECT view_name FROM all_views where owner='" + database + "'", (OracleConnection) connection))
                {
                    cmd.Transaction = transaction as OracleTransaction;
                    var r = cmd.ExecuteReader();
                
                    while (r.Read())
                        tables.Add(new DiscoveredTable(parent,r["view_name"].ToString(),querySyntaxHelper,null,TableType.View));
                }

            
            return tables.ToArray();
        }

        public override IEnumerable<DiscoveredTableValuedFunction> ListTableValuedFunctions(DiscoveredDatabase parent, IQuerySyntaxHelper querySyntaxHelper,
            DbConnection connection, string database, DbTransaction transaction = null)
        {
            return new DiscoveredTableValuedFunction[0];
        }
        
        public override DiscoveredStoredprocedure[] ListStoredprocedures(DbConnectionStringBuilder builder, string database)
        {
            return new DiscoveredStoredprocedure[0];
        }

        protected override DataTypeComputer GetDataTypeComputer(DatabaseTypeRequest request)
        {
            return new DataTypeComputer(request, OracleTypeTranslater.ExtraLengthPerNonAsciiCharacter);
        }
        protected override DataTypeComputer GetDataTypeComputer(DataColumn column)
        {
            return new DataTypeComputer(column, OracleTypeTranslater.ExtraLengthPerNonAsciiCharacter);
        }


    }
}
