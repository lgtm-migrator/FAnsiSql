﻿using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using FAnsi.Discovery;
using FAnsi.Discovery.QuerySyntax;
using FAnsi.Implementations.PostgreSql.Aggregation;
using FAnsi.Implementations.PostgreSql.Update;
using TypeGuesser;

namespace FAnsi.Implementations.PostgreSql
{
    public class PostgreSqlSyntaxHelper : QuerySyntaxHelper
    {
        public PostgreSqlSyntaxHelper() : base(new PostgreSqlTypeTranslater(), new PostgreSqlAggregateHelper(), new PostgreSqlUpdateHelper(), DatabaseType.PostgreSql)
        {
        }

        public override int MaximumDatabaseLength => 63;
        public override int MaximumTableLength => 63;
        public override int MaximumColumnLength => 63;
        
        public const string DefaultPostgresSchema = "public";

        public override string EnsureWrappedImpl(string databaseOrTableName)
        {
            return '"' + GetRuntimeName(databaseOrTableName) + '"';
        }

        public override string EnsureFullyQualified(string databaseName, string schema, string tableName)
        {
            //if there is no schema address it as db..table (which is the same as db.dbo.table in Microsoft SQL Server)
            if(string.IsNullOrWhiteSpace(schema))
                return '"'+ GetRuntimeName(databaseName) +'"'+ DatabaseTableSeparator + DefaultPostgresSchema + DatabaseTableSeparator + '"'+GetRuntimeName(tableName)+'"';

            //there is a schema so add it in
            return '"' + GetRuntimeName(databaseName) + '"' + DatabaseTableSeparator + schema + DatabaseTableSeparator + '"' + GetRuntimeName(tableName) + '"';
        }

        public override string EnsureFullyQualified(string databaseName, string schema, string tableName, string columnName,
            bool isTableValuedFunction = false)
        {
            if (isTableValuedFunction)
                return '"' + GetRuntimeName(tableName) + "\".\"" + GetRuntimeName(columnName) + "\"";//table valued functions do not support database name being in the column level selection list area of sql queries

            return EnsureFullyQualified(databaseName, schema, tableName) + ".\"" + GetRuntimeName(columnName) + '"';
        }


        public override TopXResponse HowDoWeAchieveTopX(int x)
        {
            return new TopXResponse("fetch first " + x + " rows only", QueryComponent.Postfix);
        }

        public override string GetParameterDeclaration(string proposedNewParameterName, string sqlType)
        {
            throw new NotImplementedException();
        }

        public override string GetScalarFunctionSql(MandatoryScalarFunctions function)
        {
            throw new NotImplementedException();
        }

        public override string GetAutoIncrementKeywordIfAny()
        {
            return "GENERATED ALWAYS AS IDENTITY";
        }

        public override Dictionary<string, string> GetSQLFunctionsDictionary()
        {
            throw new NotImplementedException();
        }

        public override string HowDoWeAchieveMd5(string selectSql)
        {
            throw new NotImplementedException();
        }
    }
}