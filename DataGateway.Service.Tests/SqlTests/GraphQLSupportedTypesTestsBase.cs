using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{

    [TestClass]
    public abstract class GraphQLSupportedTypesTestBase : SqlTestBase
    {
        protected const string TYPE_TABLE = "TypeTable";
        protected const string BYTE_TYPE = "byte";
        protected const string SHORT_TYPE = "short";
        protected const string INT_TYPE = "int";
        protected const string LONG_TYPE = "long";
        protected const string SINGLE_TYPE = "single";
        protected const string FLOAT_TYPE = "float";
        protected const string DECIMAL_TYPE = "decimal";
        protected const string STRING_TYPE = "string";
        protected const string BOOLEAN_TYPE = "boolean";

        #region Test Fixture Setup
        protected static GraphQLService _graphQLService;
        protected static GraphQLController _graphQLController;

        #endregion

        #region Tests

        [DataTestMethod]
        [DataRow(BYTE_TYPE, 1)]
        [DataRow(BYTE_TYPE, 2)]
        [DataRow(BYTE_TYPE, 3)]
        [DataRow(BYTE_TYPE, 4)]
        [DataRow(SHORT_TYPE, 1)]
        [DataRow(SHORT_TYPE, 2)]
        [DataRow(SHORT_TYPE, 3)]
        [DataRow(SHORT_TYPE, 4)]
        [DataRow(INT_TYPE, 1)]
        [DataRow(INT_TYPE, 2)]
        [DataRow(INT_TYPE, 3)]
        [DataRow(INT_TYPE, 4)]
        [DataRow(LONG_TYPE, 1)]
        [DataRow(LONG_TYPE, 2)]
        [DataRow(LONG_TYPE, 3)]
        [DataRow(LONG_TYPE, 4)]
        [DataRow(SINGLE_TYPE, 1)]
        [DataRow(SINGLE_TYPE, 2)]
        [DataRow(SINGLE_TYPE, 3)]
        [DataRow(SINGLE_TYPE, 4)]
        [DataRow(FLOAT_TYPE, 1)]
        [DataRow(FLOAT_TYPE, 2)]
        [DataRow(FLOAT_TYPE, 3)]
        [DataRow(FLOAT_TYPE, 4)]
        [DataRow(DECIMAL_TYPE, 1)]
        [DataRow(DECIMAL_TYPE, 2)]
        [DataRow(DECIMAL_TYPE, 3)]
        [DataRow(DECIMAL_TYPE, 4)]
        [DataRow(STRING_TYPE, 1)]
        [DataRow(STRING_TYPE, 2)]
        [DataRow(STRING_TYPE, 3)]
        [DataRow(STRING_TYPE, 4)]
        [DataRow(BOOLEAN_TYPE, 1)]
        [DataRow(BOOLEAN_TYPE, 2)]
        [DataRow(BOOLEAN_TYPE, 3)]
        [DataRow(BOOLEAN_TYPE, 4)]
        public async Task QueryTypeColumn(string type, int id)
        {
            if (!IsTypeSupportedType(type))
            {
                return;
            }

            string graphQLQueryName = "supportedType_by_pk";
            string gqlQuery = "{ supportedType_by_pk(id: " + id + ") { " + type + "_types } }";

            string dbQuery = MakeQueryOnTypeTable(new List<string> { $"{type}_types" }, id);

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);

            if (type == SINGLE_TYPE || type == FLOAT_TYPE || type == DECIMAL_TYPE)
            {
                CompareFloatResults(type, actual, expected);
            }
            else
            {
                SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
            }
        }

        [DataTestMethod]
        [DataRow(BYTE_TYPE, "255")]
        [DataRow(BYTE_TYPE, "0")]
        [DataRow(BYTE_TYPE, "null")]
        [DataRow(SHORT_TYPE, "0")]
        [DataRow(SHORT_TYPE, "30000")]
        [DataRow(SHORT_TYPE, "-30000")]
        [DataRow(SHORT_TYPE, "null")]
        [DataRow(INT_TYPE, "9999")]
        [DataRow(INT_TYPE, "0")]
        [DataRow(INT_TYPE, "-9999")]
        [DataRow(INT_TYPE, "null")]
        [DataRow(LONG_TYPE, "0")]
        [DataRow(LONG_TYPE, "9000000000000000000")]
        [DataRow(LONG_TYPE, "9000000000000000000")]
        [DataRow(LONG_TYPE, "null")]
        [DataRow(STRING_TYPE, "\"aaaaaaaaaa\"")]
        [DataRow(STRING_TYPE, "\"\"")]
        [DataRow(STRING_TYPE, "null")]
        [DataRow(SINGLE_TYPE, "-3.33")]
        [DataRow(SINGLE_TYPE, "2E35")]
        [DataRow(SINGLE_TYPE, "null")]
        [DataRow(FLOAT_TYPE, "-3.33")]
        [DataRow(FLOAT_TYPE, "2E150")]
        [DataRow(FLOAT_TYPE, "null")]
        [DataRow(DECIMAL_TYPE, "-3.333333")]
        [DataRow(DECIMAL_TYPE, "1222222.00000929292")]
        [DataRow(DECIMAL_TYPE, "null")]
        [DataRow(BOOLEAN_TYPE, "true")]
        [DataRow(BOOLEAN_TYPE, "false")]
        [DataRow(BOOLEAN_TYPE, "null")]
        public async Task InsertIntoTypeColumn(string type, string value)
        {
            if (!IsTypeSupportedType(type))
            {
                return;
            }

            string field = $"{type}_types";
            string graphQLQueryName = "createSupportedType";
            string gqlQuery = "mutation{ createSupportedType (item: {" + field + ": " + value + " }){ " + field + " } }";

            string dbQuery = MakeQueryOnTypeTable(new List<string> { field }, id: 5001);

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);

            if (type == SINGLE_TYPE || type == FLOAT_TYPE || type == DECIMAL_TYPE)
            {
                CompareFloatResults(type, actual, expected);
            }
            else
            {
                SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
            }

            await ResetDbStateAsync();
        }

        [DataTestMethod]
        [DataRow(BYTE_TYPE, "255")]
        [DataRow(BYTE_TYPE, "0")]
        [DataRow(BYTE_TYPE, "null")]
        [DataRow(SHORT_TYPE, "0")]
        [DataRow(SHORT_TYPE, "30000")]
        [DataRow(SHORT_TYPE, "-30000")]
        [DataRow(SHORT_TYPE, "null")]
        [DataRow(INT_TYPE, "9999")]
        [DataRow(INT_TYPE, "0")]
        [DataRow(INT_TYPE, "-9999")]
        [DataRow(INT_TYPE, "null")]
        [DataRow(LONG_TYPE, "0")]
        [DataRow(LONG_TYPE, "9000000000000000000")]
        [DataRow(LONG_TYPE, "9000000000000000000")]
        [DataRow(LONG_TYPE, "null")]
        [DataRow(STRING_TYPE, "\"aaaaaaaaaa\"")]
        [DataRow(STRING_TYPE, "\"\"")]
        [DataRow(STRING_TYPE, "null")]
        [DataRow(SINGLE_TYPE, "-3.33")]
        [DataRow(SINGLE_TYPE, "2E35")]
        [DataRow(SINGLE_TYPE, "null")]
        [DataRow(FLOAT_TYPE, "-3.33")]
        [DataRow(FLOAT_TYPE, "2E150")]
        [DataRow(FLOAT_TYPE, "null")]
        [DataRow(DECIMAL_TYPE, "-3.333333")]
        [DataRow(DECIMAL_TYPE, "1222222.00000929292")]
        [DataRow(DECIMAL_TYPE, "null")]
        [DataRow(BOOLEAN_TYPE, "true")]
        [DataRow(BOOLEAN_TYPE, "false")]
        [DataRow(BOOLEAN_TYPE, "null")]
        public async Task UpdateTypeColumn(string type, string value)
        {
            if (!IsTypeSupportedType(type))
            {
                return;
            }

            string field = $"{type}_types";
            string graphQLQueryName = "updateSupportedType";
            string gqlQuery = "mutation{ updateSupportedType (id: 1, item: {" + field + ": " + value + " }){ " + field + " } }";

            string dbQuery = MakeQueryOnTypeTable(new List<string> { field }, id: 1);

            string actual = await GetGraphQLResultAsync(gqlQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(dbQuery);

            if (type == SINGLE_TYPE || type == FLOAT_TYPE || type == DECIMAL_TYPE)
            {
                CompareFloatResults(type, actual, expected);
            }
            else
            {
                SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
            }

            await ResetDbStateAsync();
        }

        #endregion

        /// <summary>
        /// HotChocolate will parse large floats to exponential notation
        /// while the db will return the number fully printed out. Because
        /// the json deep compare function we are using does not account for such scenario
        /// a special comparison is needed to test floats
        /// </summary>
        private static void CompareFloatResults(string floatType, string actual, string expected)
        {
            string fieldName = $"{floatType}_types";

            using JsonDocument actualJsonDoc = JsonDocument.Parse(actual);
            using JsonDocument expectedJsonDoc = JsonDocument.Parse(expected);

            string actualFloat = actualJsonDoc.RootElement.GetProperty(fieldName).ToString();
            string expectedFloat = expectedJsonDoc.RootElement.GetProperty(fieldName).ToString();

            // handles cases when one of the values is null
            if (string.IsNullOrEmpty(actualFloat) || string.IsNullOrEmpty(expectedFloat))
            {
                Assert.AreEqual(expectedFloat, actualFloat);
                return;
            }

            switch (floatType)
            {
                case SINGLE_TYPE:
                    Assert.AreEqual(float.Parse(expectedFloat), float.Parse(actualFloat));
                    break;
                case FLOAT_TYPE:
                    Assert.AreEqual(double.Parse(expectedFloat), double.Parse(actualFloat));
                    break;
                case DECIMAL_TYPE:
                    Assert.AreEqual(decimal.Parse(expectedFloat), decimal.Parse(actualFloat));
                    break;
                default:
                    Assert.Fail($"Calling compare on unrecognized float type {floatType}");
                    break;
            }
        }

        protected abstract string MakeQueryOnTypeTable(List<string> columnsToQuery, int id);
        protected virtual bool IsTypeSupportedType(string type)
        {
            return true;
        }
    }
}
