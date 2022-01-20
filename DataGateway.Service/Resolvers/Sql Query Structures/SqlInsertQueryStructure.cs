using System;
using System.Collections.Generic;
using System.Linq;
using Azure.DataGateway.Service.Models;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// Wraps all the required data and logic to write a SQL INSERT query
    /// </summary>
    public class SqlInsertStructure
    {
        /// <summary>
        /// The name of the table the qeury will be applied on
        /// </summary>
        public string TableName { get; }

        /// <summary>
        /// Columns in which values will be inserted
        /// </summary>
        public List<string> Columns { get; }

        /// <summary>
        /// Values to insert into the given columns
        /// </summary>
        public List<string> Values { get; }

        /// <summary>
        /// Columns which will be returned from the inserted row
        /// </summary>
        public List<string> ReturnColumns { get; }

        /// <summary>
        /// Parameters required to execute the query
        /// </summary>
        public Dictionary<string, object> Parameters { get; }

        /// <summary>
        /// Used to assign unique parameter names
        /// </summary>
        public IncrementingInteger Counter { get; }

        private readonly TableDefinition _tableDefinition;
        private readonly IQueryBuilder _queryBuilder;

        public SqlInsertStructure(string tableName, TableDefinition tableDefinition, IDictionary<string, object> mutationParams, IQueryBuilder queryBuilder)
        {
            TableName = tableName;
            Columns = new();
            Values = new();
            Parameters = new();
            Counter = new();

            _tableDefinition = tableDefinition;
            _queryBuilder = queryBuilder;

            foreach (KeyValuePair<string, object> param in mutationParams)
            {
                if (param.Value == null)
                {
                    continue;
                }

                PopulateColumnsAndParams(param.Key, param.Value);
            }

            // If none of the column values were specified as one of the params,
            // we need to explicitly add. This scenario is possible for REST request.
            if (Columns.Count == 0)
            {
                List<string> allColumns = new(tableDefinition.Columns.Keys);

                // The assumption here is primary key columns need not be specified
                // since their value will be autogenerated.
                // More columns can be excluded if the metadata provides us information
                // that they have default values.
                IEnumerable<string> columnsToBeAdded = allColumns.Except(tableDefinition.PrimaryKey);
                foreach (string column in columnsToBeAdded)
                {
                    // Using null as the default value.
                    PopulateColumnsAndParams(column, value: null);
                }
            }

            // return primary key so the inserted row can be identified
            ReturnColumns = _tableDefinition.PrimaryKey.Select(primaryKey => QuoteIdentifier(primaryKey)).ToList();
        }

        /// <summary>
        /// Populate the column names in the Columns, create parameter and add its value
        /// into the Parameters dictionary.
        /// </summary>
        private void PopulateColumnsAndParams(string columnName, object value)
        {
            Columns.Add(QuoteIdentifier(columnName));

            string paramName = $"param{Counter.Next()}";
            Values.Add($"@{paramName}");
            if (value != null)
            {
                Parameters.Add(paramName, GetParamValueBasedOnColumnType(columnName, value.ToString()));
            }
            else
            {
                Parameters.Add(paramName, value: null);
            }
        }

        /// <summary>
        /// QuoteIdentifier simply forwards to the QuoteIdentifier
        /// implementation of the querybuilder that this query structure uses.
        /// So it wrapse the string in double quotes for Postgres and square
        /// brackets for MSSQL.
        /// </summary>
        private string QuoteIdentifier(string ident)
        {
            return _queryBuilder.QuoteIdentifier(ident);
        }

        /// <summary>
        /// Used to identify the columns in which to insert values
        /// INSERT INTO {TableName} {ColumnsSql} VALUES ...
        /// </summary>
        public string ColumnsSql()
        {
            return "(" + string.Join(", ", Columns) + ")";
        }

        /// <summary>
        /// Creates the SLQ code for the inserted values
        /// INSERT INTO ... VALUES {ValuesSql}
        /// </summary>
        public string ValuesSql()
        {
            return "(" + string.Join(", ", Values) + ")";
        }

        /// <summary>
        /// Returns quote identified column names seperated by commas
        /// Used by Postgres like
        /// INSET INTO ... VALUES ... RETURNING {ReturnColumnsSql}
        /// </summary>
        public string ReturnColumnsSql()
        {
            return string.Join(", ", ReturnColumns);
        }

        /// <summary>
        /// Converts the query structure to the actual query string.
        /// </summary>
        public override string ToString()
        {
            return _queryBuilder.Build(this);
        }

        ///<summary>
        /// Resolves a string parameter to the correct type, by using the type of the field
        /// it is supposed to be compared with
        ///</summary>
        object GetParamValueBasedOnColumnType(string columnName, string param)
        {
            string type = _tableDefinition.Columns.GetValueOrDefault(columnName).Type;
            switch (type)
            {
                case "text":
                case "varchar":
                    return param;
                case "bigint":
                case "int":
                case "smallint":
                    return long.Parse(param);
                default:
                    throw new Exception($"Type of field \"{type}\" could not be determined");
            }
        }
    }
}
