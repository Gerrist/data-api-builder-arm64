using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.GraphQLBuilder.Directives;
using HotChocolate.Language;
using HotChocolate.Types;

namespace Azure.DataGateway.Service.GraphQLBuilder
{
    public static class GraphQLUtils
    {
        public const string DEFAULT_PRIMARY_KEY_NAME = "id";

        public static bool IsModelType(ObjectTypeDefinitionNode objectTypeDefinitionNode)
        {
            string modelDirectiveName = ModelDirectiveType.DirectiveName;
            return objectTypeDefinitionNode.Directives.Any(d => d.Name.ToString() == modelDirectiveName);
        }

        public static bool IsModelType(ObjectType objectType)
        {
            string modelDirectiveName = ModelDirectiveType.DirectiveName;
            return objectType.Directives.Any(d => d.Name.ToString() == modelDirectiveName);
        }

        public static bool IsBuiltInType(ITypeNode typeNode)
        {
            HashSet<string> inBuiltTypes = new()
            {
                "ID",
                "Byte",
                "Short",
                "Int",
                "Long",
                "Single",
                "Float",
                "Decimal",
                "String",
                "Boolean"
            };
            string name = typeNode.NamedType().Name.Value;
            return inBuiltTypes.Contains(name);
        }

        /// <summary>
        /// Find all the primary keys for a given object node
        /// using the information available in the directives.
        /// If no directives present, default to a field named "id" as the primary key.
        /// If even that doesn't exist, throw an exception in initialization.
        /// </summary>
        public static List<FieldDefinitionNode> FindPrimaryKeyFields(ObjectTypeDefinitionNode node)
        {
            List<FieldDefinitionNode> fieldDefinitionNodes =
                new(node.Fields.Where(f => f.Directives.Any(d => d.Name.Value == PrimaryKeyDirectiveType.DirectiveName)));

            // By convention we look for a `@primaryKey` directive, if that didn't exist
            // fallback to using an expected field name on the GraphQL object
            if (fieldDefinitionNodes.Count == 0)
            {
                FieldDefinitionNode? fieldDefinitionNode =
                    node.Fields.FirstOrDefault(f => f.Name.Value == DEFAULT_PRIMARY_KEY_NAME);
                if (fieldDefinitionNode is not null)
                {
                    fieldDefinitionNodes.Add(fieldDefinitionNode);
                }
            }

            // Nothing explicitly defined nor could we find anything using our conventions, fail out
            if (fieldDefinitionNodes.Count == 0)
            {
                throw new DataGatewayException(
                    message: "No primary key defined and conventions couldn't locate a fallback",
                    subStatusCode: DataGatewayException.SubStatusCodes.ErrorInInitialization,
                    statusCode: System.Net.HttpStatusCode.ServiceUnavailable);
            }

            return fieldDefinitionNodes;
        }

        /// <summary>
        /// Checks if a field is auto generated by the database using the directives of the field definition.
        /// </summary>
        /// <param name="field">Field definition to check.</param>
        /// <returns><c>true</c> if it is auto generated, <c>false</c> if it is not.</returns>
        public static bool IsAutoGeneratedField(FieldDefinitionNode field)
        {
            return field.Directives.Any(d => d.Name.Value == AutoGeneratedDirectiveType.DirectiveName);
        }
    }
}
