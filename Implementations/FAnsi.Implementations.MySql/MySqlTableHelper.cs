﻿using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;
using FAnsi.Connections;
using FAnsi.Discovery;
using FAnsi.Discovery.Constraints;
using FAnsi.Naming;
using MySql.Data.MySqlClient;

namespace FAnsi.Implementations.MySql
{
    public class MySqlTableHelper : DiscoveredTableHelper
    {
        private readonly static Regex IntParentheses = new Regex(@"^int\(\d+\)", RegexOptions.IgnoreCase);
        private readonly static Regex SmallintParentheses = new Regex(@"^smallint\(\d+\)", RegexOptions.IgnoreCase);
        private readonly static Regex BitParentheses = new Regex(@"^bit\(\d+\)", RegexOptions.IgnoreCase);

        public override DiscoveredColumn[] DiscoverColumns(DiscoveredTable discoveredTable, IManagedConnection connection, string database)
        {
            List<DiscoveredColumn> columns = new List<DiscoveredColumn>();
            var tableName = discoveredTable.GetRuntimeName();
            
            DbCommand cmd = discoveredTable.Database.Server.Helper.GetCommand("SHOW FULL COLUMNS FROM `" + database + "`.`" + tableName + "`", connection.Connection);
            cmd.Transaction = connection.Transaction;

            using(DbDataReader r = cmd.ExecuteReader())
            {
                if (!r.HasRows)
                    throw new Exception("Could not find any columns for table " + tableName + " in database " + database);

                while (r.Read())
                {
                    var toAdd = new DiscoveredColumn(discoveredTable, (string) r["Field"],YesNoToBool(r["Null"]));

                    if (r["Key"].Equals("PRI"))
                        toAdd.IsPrimaryKey = true;
                    
                    toAdd.IsAutoIncrement = r["Extra"] as string == "auto_increment";
                    toAdd.Collation = r["Collation"] as string;
                    
                    toAdd.DataType = new DiscoveredDataType(r,TrimIntDisplayValues(r["Type"].ToString()),toAdd);
                    columns.Add(toAdd);

                }

                r.Close();
            }

            return columns.ToArray();
            
        }

        private bool YesNoToBool(object o)
        {
            if (o is bool)
                return (bool)o;

            if (o == null || o == DBNull.Value)
                return false;

            if (o.ToString() == "NO")
                return false;
            
            if (o.ToString() == "YES")
                return true;

            return Convert.ToBoolean(o);
        }



        private string TrimIntDisplayValues(string type)
        {
            //See comments of int(5) means display 5 digits only it doesn't prevent storing larger numbers: https://stackoverflow.com/a/5634147/4824531

            if (IntParentheses.IsMatch(type))
                return IntParentheses.Replace(type, "int");

            if (SmallintParentheses.IsMatch(type))
                return SmallintParentheses.Replace(type, "smallint");

            if (BitParentheses.IsMatch(type))
                return BitParentheses.Replace(type, "bit");

            return type;
        }

        public override IDiscoveredColumnHelper GetColumnHelper()
        {
            return new MySqlColumnHelper();
        }

        public override void DropTable(DbConnection connection, DiscoveredTable table)
        {
            var cmd = new MySqlCommand("drop table " + table.GetFullyQualifiedName(), (MySqlConnection)connection);
            cmd.ExecuteNonQuery();
        }

        public override void DropColumn(DbConnection connection, DiscoveredColumn columnToDrop)
        {
            var cmd = new MySqlCommand("alter table " + columnToDrop.Table.GetFullyQualifiedName() + " drop column " + columnToDrop.GetRuntimeName(), (MySqlConnection)connection);
            cmd.ExecuteNonQuery();
        }

        public override int GetRowCount(DbConnection connection, IHasFullyQualifiedNameToo table, DbTransaction dbTransaction = null)
        {
            var cmd = new MySqlCommand("select count(*) from " + table.GetFullyQualifiedName(),(MySqlConnection) connection);
            cmd.Transaction = dbTransaction as MySqlTransaction;
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public override DiscoveredParameter[] DiscoverTableValuedFunctionParameters(DbConnection connection,
            DiscoveredTableValuedFunction discoveredTableValuedFunction, DbTransaction transaction)
        {
            throw new NotImplementedException();
        }

        public override IBulkCopy BeginBulkInsert(DiscoveredTable discoveredTable,IManagedConnection connection)
        {
            return new MySqlBulkCopy(discoveredTable, connection);
        }

        public override DiscoveredRelationship[] DiscoverRelationships(DiscoveredTable table, DbConnection connection,IManagedTransaction transaction = null)
        {
            string sql = @"SELECT 
u.CONSTRAINT_NAME,
u.TABLE_SCHEMA,
u.TABLE_NAME,
u.COLUMN_NAME,
u.REFERENCED_TABLE_SCHEMA,
u.REFERENCED_TABLE_NAME,
u.REFERENCED_COLUMN_NAME,
c.DELETE_RULE
FROM
    INFORMATION_SCHEMA.KEY_COLUMN_USAGE u
INNER JOIN
    INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS c ON c.CONSTRAINT_NAME = u.CONSTRAINT_NAME
WHERE
  u.REFERENCED_TABLE_SCHEMA = @db AND
  u.REFERENCED_TABLE_NAME = @tbl";

            var cmd = new MySqlCommand(sql, (MySqlConnection) connection);
            
            var p = new MySqlParameter("@db", MySqlDbType.String);
            p.Value = table.Database.GetRuntimeName();
            cmd.Parameters.Add(p);

            p = new MySqlParameter("@tbl", MySqlDbType.String);
            p.Value = table.GetRuntimeName();
            cmd.Parameters.Add(p);

            Dictionary<string,DiscoveredRelationship> toReturn = new Dictionary<string,DiscoveredRelationship>();
            
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    var fkName = r["CONSTRAINT_NAME"].ToString();
                    
                    DiscoveredRelationship current;

                    //could be a 2+ columns foreign key?
                    if (toReturn.ContainsKey(fkName))
                    {
                        current = toReturn[fkName];
                    }
                    else
                    {
                        
                        var pkDb = r["REFERENCED_TABLE_SCHEMA"].ToString();
                        var pkTableName = r["REFERENCED_TABLE_NAME"].ToString();

                        var fkDb = r["TABLE_SCHEMA"].ToString();
                        var fkTableName =  r["TABLE_NAME"].ToString();

                        var pktable = table.Database.Server.ExpectDatabase(pkDb).ExpectTable(pkTableName);
                        var fktable = table.Database.Server.ExpectDatabase(fkDb).ExpectTable(fkTableName);

                        //https://dev.mysql.com/doc/refman/8.0/en/referential-constraints-table.html
                        var deleteRuleString = r["DELETE_RULE"].ToString();

                        CascadeRule deleteRule = CascadeRule.Unknown;
                        
                        if(deleteRuleString == "CASCADE")
                            deleteRule = CascadeRule.Delete;
                        else if(deleteRuleString == "NO ACTION")
                            deleteRule = CascadeRule.NoAction;
                        else if(deleteRuleString == "RESTRICT")
                            deleteRule = CascadeRule.NoAction;
                        else if (deleteRuleString == "SET NULL")
                            deleteRule = CascadeRule.SetNull;
                        else if (deleteRuleString == "SET DEFAULT")
                            deleteRule = CascadeRule.SetDefault;

                        current = new DiscoveredRelationship(fkName,pktable,fktable,deleteRule);
                        toReturn.Add(current.Name,current);
                    }

                    current.AddKeys(r["REFERENCED_COLUMN_NAME"].ToString(), r["COLUMN_NAME"].ToString(), transaction);
                }
            }
            
            return toReturn.Values.ToArray();
        }

        protected override string GetRenameTableSql(DiscoveredTable discoveredTable, string newName)
        {
            return string.Format("RENAME TABLE `{0}` TO `{1}`;", discoveredTable.GetRuntimeName(), newName);
        }

        public override string GetTopXSqlForTable(IHasFullyQualifiedNameToo table, int topX)
        {
            return "SELECT * FROM " + table.GetFullyQualifiedName() + " LIMIT " + topX;
        }


        public override void DropFunction(DbConnection connection, DiscoveredTableValuedFunction functionToDrop)
        {
            throw new NotImplementedException();
        }
    }
}