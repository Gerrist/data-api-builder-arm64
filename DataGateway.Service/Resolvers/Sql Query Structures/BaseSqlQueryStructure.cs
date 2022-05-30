using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;

namespace Azure.DataGateway.Service.Resolvers
{

    /// <summary>
    /// Holds shared properties and methods among
    /// Sql*QueryStructure classes
    /// </summary>
    public abstract class BaseSqlQueryStructure : BaseQueryStructure
    {
        protected ISqlMetadataProvider SqlMetadataProvider { get; }

        /// <summary>
        /// The Entity associated with this query.
        /// </summary>
        public string EntityName { get; protected set; }

        /// <summary>
        /// The DatabaseObject associated with the entity, represents the
        /// databse object to be queried.
        /// </summary>
        public DatabaseObject DatabaseObject { get; }

        /// <summary>
        /// The alias of the main table to be queried.
        /// </summary>
        public string TableAlias { get; protected set; }

        /// <summary>
        /// FilterPredicates is a string that represents the filter portion of our query
        /// in the WHERE Clause. This is generated specifically from the $filter portion
        /// of the query string.
        /// </summary>
        public string? FilterPredicates { get; set; }

        public BaseSqlQueryStructure(
            ISqlMetadataProvider sqlMetadataProvider,
            string entityName,
            IncrementingInteger? counter = null)
            : base(counter)
        {
            SqlMetadataProvider = sqlMetadataProvider;
            if (!string.IsNullOrEmpty(entityName))
            {
                EntityName = entityName;
                DatabaseObject = sqlMetadataProvider.EntityToDatabaseObject[entityName];
            }
            else
            {
                EntityName = string.Empty;
                DatabaseObject = new();
            }

            // Default the alias to the empty string since this base construtor
            // is called for requests other than Find operations. We only use
            // TableAlias for Find, so we leave empty here and then populate
            // in the Find specific contructor.
            TableAlias = string.Empty;
        }

        /// <summary>
        /// For UPDATE (OVERWRITE) operation
        /// Adds result of (TableDefinition.Columns minus MutationFields) to UpdateOperations with null values
        /// There will not be any columns leftover that are PK, since they are handled in request validation.
        /// </summary>
        /// <param name="leftoverSchemaColumns"></param>
        /// <param name="updateOperations">List of Predicates representing UpdateOperations.</param>
        /// <param name="tableDefinition">The definition for the table.</param>
        public void AddNullifiedUnspecifiedFields(List<string> leftoverSchemaColumns, List<Predicate> updateOperations, TableDefinition tableDefinition)
        {
            //result of adding (TableDefinition.Columns - MutationFields) to UpdateOperations
            foreach (string leftoverColumn in leftoverSchemaColumns)
            {
                // If the left over column is autogenerated
                // then no need to add it with a null value.
                if (tableDefinition.Columns[leftoverColumn].IsAutoGenerated)
                {
                    continue;
                }

                else
                {
                    Predicate predicate = new(
                        new PredicateOperand(new Column(tableSchema: DatabaseObject.SchemaName, tableName: DatabaseObject.Name, leftoverColumn)),
                        PredicateOperation.Equal,
                        new PredicateOperand($"@{MakeParamWithValue(value: null)}")
                    );

                    updateOperations.Add(predicate);
                }
            }
        }

        /// <summary>
        /// Get column type from table underlying the query strucutre
        /// </summary>
        public Type GetColumnSystemType(string columnName)
        {
            if (GetUnderlyingTableDefinition().Columns.TryGetValue(columnName, out ColumnDefinition? column))
            {
                return column.SystemType;
            }
            else
            {
                throw new ArgumentException($"{columnName} is not a valid column of {DatabaseObject.Name}");
            }
        }

        /// <summary>
        /// Returns the TableDefinition for the the table of this query.
        /// </summary>
        protected TableDefinition GetUnderlyingTableDefinition()
        {
            return SqlMetadataProvider.GetTableDefinition(EntityName);
        }

        /// <summary>
        /// Get primary key as list of string
        /// </summary>
        public List<string> PrimaryKey()
        {
            return GetUnderlyingTableDefinition().PrimaryKey;
        }

        /// <summary>
        /// get all columns of the table
        /// </summary>
        public List<string> AllColumns()
        {
            return GetUnderlyingTableDefinition().Columns.Select(col => col.Key).ToList();
        }

        ///<summary>
        /// Gets the value of the parameter cast as the system type
        /// of the column this parameter is associated with
        ///</summary>
        /// <exception cref="ArgumentException">columnName is not a valid column of table or param
        /// does not have a valid value type</exception>
        protected object GetParamAsColumnSystemType(string param, string columnName)
        {
            Type systemType = GetColumnSystemType(columnName);
            try
            {
                switch (Type.GetTypeCode(systemType))
                {
                    case TypeCode.String:
                        return param;
                    case TypeCode.Byte:
                        return byte.Parse(param);
                    case TypeCode.Int16:
                        return short.Parse(param);
                    case TypeCode.Int32:
                        return int.Parse(param);
                    case TypeCode.Int64:
                        return long.Parse(param);
                    case TypeCode.Single:
                        return float.Parse(param);
                    case TypeCode.Double:
                        return double.Parse(param);
                    case TypeCode.Decimal:
                        return decimal.Parse(param);
                    case TypeCode.Boolean:
                        return Boolean.Parse(param);
                    default:
                        // should never happen due to the config being validated for correct types
                        throw new NotSupportedException($"{systemType.Name} is not supported");
                }
            }
            catch (Exception e)
            {
                if (e is FormatException ||
                    e is ArgumentNullException ||
                    e is OverflowException)
                {
                    throw new ArgumentException($"Parameter \"{param}\" cannot be resolved as column \"{columnName}\" " +
                        $"with type \"{systemType.Name}\".");
                }

                throw;
            }
        }

        /// <summary>
        /// Creates the dictionary of fields and their values
        /// to be set in the mutation from the MutationInput argument name "item".
        /// This is only applicable for GraphQL since the input we get from the request
        /// is of the EntityInput object form.
        /// For REST, we simply get the mutation values in the request body as is - so
        /// we will not find the argument of name "item" in the mutationParams.
        /// </summary>
        /// <exception cref="InvalidDataException"></exception>
        internal static IDictionary<string, object?> InputArgumentToMutationParams(
            IDictionary<string, object?> mutationParams, string argumentName)
        {
            if (mutationParams.TryGetValue(argumentName, out object? item))
            {
                Dictionary<string, object?> mutationInput;
                // An inline argument was set
                // TODO: This assumes the input was NOT nullable.
                if (item is List<ObjectFieldNode> mutationInputRaw)
                {
                    mutationInput = new Dictionary<string, object?>();
                    foreach (ObjectFieldNode node in mutationInputRaw)
                    {
                        mutationInput.Add(node.Name.Value, node.Value.Value);
                    }
                }
                // Variables were provided to the mutation
                else if (item is Dictionary<string, object?> dict)
                {
                    mutationInput = dict;
                }
                else
                {
                    throw new DataGatewayException(
                        message: "The type of argument for the provided data is unsupported.",
                        subStatusCode: DataGatewayException.SubStatusCodes.BadRequest,
                        statusCode: HttpStatusCode.BadRequest);
                }

                return mutationInput;
            }

            // Its ok to not find the input argument name in the mutation params dictionary
            // because it indicates the REST scenario.
            return mutationParams;
        }
    }
}
