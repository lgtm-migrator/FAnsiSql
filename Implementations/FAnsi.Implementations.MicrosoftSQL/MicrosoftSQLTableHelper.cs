﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using System.Globalization;
using System.Linq;
using FAnsi.Connections;
using FAnsi.Discovery;
using FAnsi.Discovery.Constraints;
using FAnsi.Exceptions;
using FAnsi.Naming;

namespace FAnsi.Implementations.MicrosoftSQL
{
    public class MicrosoftSQLTableHelper : DiscoveredTableHelper
    {
        public override DiscoveredColumn[] DiscoverColumns(DiscoveredTable discoveredTable, IManagedConnection connection, string database)
        {
            List<DiscoveredColumn> toReturn = new List<DiscoveredColumn>();

            using (DbCommand cmd = discoveredTable.GetCommand("use [" + database + @"];
SELECT  
sys.columns.name AS COLUMN_NAME,
 sys.types.name AS TYPE_NAME,
  sys.columns.collation_name AS COLLATION_NAME,
   sys.columns.max_length as LENGTH,
   sys.columns.scale as SCALE,
    sys.columns.is_identity,
    sys.columns.is_nullable,
   sys.columns.precision as PRECISION,
sys.columns.collation_name
from sys.columns 
join 
sys.types on sys.columns.user_type_id = sys.types.user_type_id
where object_id = OBJECT_ID(@tableName)", connection.Connection, connection.Transaction))
            {
                var p = cmd.CreateParameter();
                p.ParameterName = "@tableName";
                p.Value = GetObjectName(discoveredTable);
                cmd.Parameters.Add(p);

                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                    {
                        bool isNullable = Convert.ToBoolean(r["is_nullable"]);

                        //if it is a table valued function prefix the column name with the table valued function name
                        string columnName = discoveredTable is DiscoveredTableValuedFunction
                            ? discoveredTable.GetRuntimeName() + "." + r["COLUMN_NAME"]
                            : r["COLUMN_NAME"].ToString();

                        var toAdd = new DiscoveredColumn(discoveredTable, columnName, isNullable);
                        toAdd.IsAutoIncrement = Convert.ToBoolean(r["is_identity"]);

                        toAdd.DataType = new DiscoveredDataType(r, GetSQLType_FromSpColumnsResult(r), toAdd);
                        toAdd.Collation = r["collation_name"] as string;
                        toReturn.Add(toAdd);
                    }
            }

            if(!toReturn.Any())
                throw new Exception("Could not find any columns in table " + discoveredTable);
            
            //don't bother looking for pks if it is a table valued function
            if (discoveredTable is DiscoveredTableValuedFunction)
                return toReturn.ToArray();
            
            var pks = ListPrimaryKeys(connection, discoveredTable);

            foreach (DiscoveredColumn c in toReturn)
                if (pks.Any(pk=>pk.Equals(c.GetRuntimeName())))
                    c.IsPrimaryKey = true;


            return toReturn.ToArray();
        }

        /// <summary>
        /// Returns the table name suitable for being passed into OBJECT_ID including schema if any
        /// </summary>
        /// <param name="table"></param>
        /// <returns></returns>
        private string GetObjectName(DiscoveredTable table)
        {
            var syntax = table.GetQuerySyntaxHelper();

            var objectName = syntax.EnsureWrapped(table.GetRuntimeName());

            if (table.Schema != null)
                return syntax.EnsureWrapped(table.Schema) + "." + objectName;

            return objectName;
        }

        public override IDiscoveredColumnHelper GetColumnHelper()
        {
            return new MicrosoftSQLColumnHelper();
        }

        public override void DropTable(DbConnection connection, DiscoveredTable tableToDrop)
        {
            
            SqlCommand cmd;

            switch (tableToDrop.TableType)
            {
                case TableType.View:
                    if (connection.Database != tableToDrop.Database.GetRuntimeName())
                        connection.ChangeDatabase(tableToDrop.GetRuntimeName());

                    if(!connection.Database.ToLower().Equals(tableToDrop.Database.GetRuntimeName().ToLower()))
                        throw new NotSupportedException("Cannot drop view "+tableToDrop +" because it exists in database "+ tableToDrop.Database.GetRuntimeName() +" while the current current database connection is pointed at database:" + connection.Database + " (use .ChangeDatabase on the connection first) - SQL Server does not support cross database view dropping");

                    cmd = new SqlCommand("DROP VIEW " + tableToDrop.GetWrappedName(), (SqlConnection)connection);
                    break;
                case TableType.Table:
                    cmd = new SqlCommand("DROP TABLE " + tableToDrop.GetFullyQualifiedName(), (SqlConnection)connection);
                    break;
                case TableType.TableValuedFunction :
                    DropFunction(connection,(DiscoveredTableValuedFunction) tableToDrop);
                    return;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            using(cmd)
                cmd.ExecuteNonQuery();
        }

        public override void DropFunction(DbConnection connection, DiscoveredTableValuedFunction functionToDrop)
        {
            using(SqlCommand cmd = new SqlCommand($"DROP FUNCTION {functionToDrop.Schema??"dbo"}.{functionToDrop.GetRuntimeName()}", (SqlConnection)connection))
                cmd.ExecuteNonQuery();
        }

        public override void DropColumn(DbConnection connection, DiscoveredColumn columnToDrop)
        {
            using(SqlCommand cmd = new SqlCommand("ALTER TABLE " + columnToDrop.Table.GetFullyQualifiedName() + " DROP column " + columnToDrop.GetWrappedName(), (SqlConnection)connection))
                cmd.ExecuteNonQuery();
        }

        
        public override DiscoveredParameter[] DiscoverTableValuedFunctionParameters(DbConnection connection,DiscoveredTableValuedFunction discoveredTableValuedFunction, DbTransaction transaction)
        {
            List<DiscoveredParameter> toReturn = new List<DiscoveredParameter>();

            string query =
                @"select 
sys.parameters.name AS name,
sys.types.name AS TYPE_NAME,
sys.parameters.max_length AS LENGTH,
sys.types.collation_name AS COLLATION_NAME,
sys.parameters.scale AS SCALE,
sys.parameters.precision AS PRECISION
 from 
sys.parameters 
join
sys.types on sys.parameters.user_type_id = sys.types.user_type_id
where object_id = OBJECT_ID(@tableName)";

            using (DbCommand cmd = discoveredTableValuedFunction.GetCommand(query, connection))
            {
                var p = cmd.CreateParameter();
                p.ParameterName = "@tableName";
                p.Value = GetObjectName(discoveredTableValuedFunction);
                cmd.Parameters.Add(p);

                cmd.Transaction = transaction;

                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        DiscoveredParameter toAdd = new DiscoveredParameter(r["name"].ToString());
                        toAdd.DataType = new DiscoveredDataType(r, GetSQLType_FromSpColumnsResult(r),null);
                        toReturn.Add(toAdd);
                    }
                }
            }
            
            return toReturn.ToArray();
        }

        public override IBulkCopy BeginBulkInsert(DiscoveredTable discoveredTable,IManagedConnection connection,CultureInfo culture)
        {
            return new MicrosoftSQLBulkCopy(discoveredTable,connection,culture);
        }

        public override void CreatePrimaryKey(DatabaseOperationArgs args, DiscoveredTable table, DiscoveredColumn[] discoverColumns)
        {
            try
            {
                using (var connection = args.GetManagedConnection(table))
                {
                    var columnHelper = GetColumnHelper();
                    foreach (var col in discoverColumns.Where(dc => dc.AllowNulls))
                    {
                        var alterSql = columnHelper.GetAlterColumnToSql(col, col.DataType.SQLType, false);
                        using(var alterCmd = table.GetCommand(alterSql, connection.Connection, connection.Transaction))
                            args.ExecuteNonQuery(alterCmd);
                    }
                }
            }
            catch (Exception e)
            {
                throw new AlterFailedException(string.Format(FAnsiStrings.DiscoveredTableHelper_CreatePrimaryKey_Failed_to_create_primary_key_on_table__0__using_columns___1__, table, string.Join(",", discoverColumns.Select(c => c.GetRuntimeName()))), e);
            }

            base.CreatePrimaryKey(args,table, discoverColumns);
        }

        public override DiscoveredRelationship[] DiscoverRelationships(DiscoveredTable table,DbConnection connection, IManagedTransaction transaction = null)
        {
            var toReturn = new Dictionary<string,DiscoveredRelationship>();

            string sql = "exec sp_fkeys @pktable_name = @table, @pktable_qualifier=@database, @pktable_owner=@schema";

            using (DbCommand cmd = table.GetCommand(sql, connection))
            {
                if(transaction != null)
                    cmd.Transaction = transaction.Transaction;
            
                var p = cmd.CreateParameter();
                p.ParameterName = "@table";
                p.Value = table.GetRuntimeName();
                p.DbType = DbType.String;
                cmd.Parameters.Add(p);

                p = cmd.CreateParameter();
                p.ParameterName = "@schema";
                p.Value = table.Schema ?? "dbo";
                p.DbType = DbType.String;
                cmd.Parameters.Add(p);
                
                p = cmd.CreateParameter();
                p.ParameterName = "@database";
                p.Value = table.Database.GetRuntimeName();
                p.DbType = DbType.String;
                cmd.Parameters.Add(p);

                using (DataTable dt = new DataTable())
                {
                    var da = table.Database.Server.GetDataAdapter(cmd);
                    da.Fill(dt);
                    
                    foreach(DataRow r in dt.Rows)
                    {
                        var fkName = r["FK_NAME"].ToString();
                        
                        DiscoveredRelationship current;

                        //could be a 2+ columns foreign key?
                        if (toReturn.ContainsKey(fkName))
                        {
                            current = toReturn[fkName];
                        }
                        else
                        {
                            var pkdb = r["PKTABLE_QUALIFIER"].ToString();
                            var pkschema = r["PKTABLE_OWNER"].ToString();
                            var pktableName = r["PKTABLE_NAME"].ToString();

                            var pktable = table.Database.Server.ExpectDatabase(pkdb).ExpectTable(pktableName, pkschema);

                            var fkdb = r["FKTABLE_QUALIFIER"].ToString();
                            var fkschema = r["FKTABLE_OWNER"].ToString();
                            var fktableName = r["FKTABLE_NAME"].ToString();

                            var fktable = table.Database.Server.ExpectDatabase(fkdb).ExpectTable(fktableName, fkschema);

                            var deleteRuleInt = Convert.ToInt32(r["DELETE_RULE"]);
                            CascadeRule deleteRule = CascadeRule.Unknown;
                            

                            if(deleteRuleInt == 0)
                                deleteRule = CascadeRule.Delete;
                            else if(deleteRuleInt == 1)
                                deleteRule = CascadeRule.NoAction;
                            else if (deleteRuleInt == 2)
                                deleteRule = CascadeRule.SetNull;
                            else if (deleteRuleInt == 3)
                                deleteRule = CascadeRule.SetDefault;

                            
                            /*
        https://docs.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-fkeys-transact-sql?view=sql-server-2017
                             
        0=CASCADE changes to foreign key.
        1=NO ACTION changes if foreign key is present.
        2 = set null
        3 = set default*/

                            current = new DiscoveredRelationship(fkName,pktable,fktable,deleteRule);
                            toReturn.Add(current.Name,current);
                        }

                        current.AddKeys(r["PKCOLUMN_NAME"].ToString(), r["FKCOLUMN_NAME"].ToString(),transaction);
                    }
                }
            }
            
            return toReturn.Values.ToArray();

        }

        protected override string GetRenameTableSql(DiscoveredTable discoveredTable, string newName)
        {
            string oldName = discoveredTable.GetWrappedName();
            
            var syntax = discoveredTable.GetQuerySyntaxHelper();

            if (!string.IsNullOrWhiteSpace(discoveredTable.Schema))
                oldName = syntax.EnsureWrapped( discoveredTable.Schema) + "." + oldName;

            return string.Format("exec sp_rename '{0}', '{1}'", syntax.Escape(oldName), syntax.Escape(newName));
        }

        public override void MakeDistinct(DatabaseOperationArgs args,DiscoveredTable discoveredTable)
        {
            var syntax = discoveredTable.GetQuerySyntaxHelper();

            string sql = 
            @"DELETE f
            FROM (
            SELECT	ROW_NUMBER() OVER (PARTITION BY {0} ORDER BY {0}) AS RowNum
            FROM {1}
            
            ) as f
            where RowNum > 1";
            
            string columnList = string.Join(",",
                discoveredTable.DiscoverColumns().Select(c=>syntax.EnsureWrapped(c.GetRuntimeName())));

            string sqlToExecute = string.Format(sql,columnList,discoveredTable.GetFullyQualifiedName());

            var server = discoveredTable.Database.Server;

            using (var con = args.GetManagedConnection(server))
            {
                using(var cmd = server.GetCommand(sqlToExecute, con))
                    args.ExecuteNonQuery(cmd);
            }
        }


        public override string GetTopXSqlForTable(IHasFullyQualifiedNameToo table, int topX)
        {
            return "SELECT TOP " + topX + " * FROM " + table.GetFullyQualifiedName();
        }
        
        private string GetSQLType_FromSpColumnsResult(DbDataReader r)
        {
            string columnType = r["TYPE_NAME"] as string;
            string lengthQualifier = "";
            
            if (HasPrecisionAndScale(columnType))
                lengthQualifier = "(" + r["PRECISION"] + "," + r["SCALE"] + ")";
            else
                if (RequiresLength(columnType))
                {
                    lengthQualifier = "(" + AdjustForUnicodeAndNegativeOne(columnType,Convert.ToInt32(r["LENGTH"])) + ")";
                }

            if (columnType == "text")
                return "varchar(max)";

            return columnType + lengthQualifier;
        }

        private object AdjustForUnicodeAndNegativeOne(string columnType, int length)
        {
            if (length == -1)
                return "max";

            if (columnType.Contains("nvarchar") || columnType.Contains("nchar") || columnType.Contains("ntext"))
                return length/2;

            return length;
        }


        private string[] ListPrimaryKeys(IManagedConnection con, DiscoveredTable table)
        {
            List<string> toReturn = new List<string>();

            string query = @"SELECT i.name AS IndexName, 
OBJECT_NAME(ic.OBJECT_ID) AS TableName, 
COL_NAME(ic.OBJECT_ID,ic.column_id) AS ColumnName, 
c.is_identity
FROM sys.indexes AS i 
INNER JOIN sys.index_columns AS ic 
INNER JOIN sys.columns AS c ON ic.object_id = c.object_id AND ic.column_id = c.column_id 
ON i.OBJECT_ID = ic.OBJECT_ID 
AND i.index_id = ic.index_id 
WHERE (i.is_primary_key = 1) AND ic.OBJECT_ID = OBJECT_ID(@tableName)
ORDER BY OBJECT_NAME(ic.OBJECT_ID), ic.key_ordinal";

            using (DbCommand cmd = table.GetCommand(query, con.Connection))
            {
                var p = cmd.CreateParameter();
                p.ParameterName = "@tableName";
                p.Value = GetObjectName(table);
                cmd.Parameters.Add(p);

                cmd.Transaction = con.Transaction;
                using(DbDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        toReturn.Add((string) r["ColumnName"]);

                    r.Close();
                }
            }

            
            return toReturn.ToArray();
        }
    }

}