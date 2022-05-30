using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.GraphQLBuilder.Directives;
using Azure.DataGateway.Service.GraphQLBuilder.Queries;
using HotChocolate.Language;
using static Azure.DataGateway.Service.GraphQLBuilder.GraphQLNaming;

namespace Azure.DataGateway.Service.GraphQLBuilder.Sql
{
    public static class SchemaConverter
    {
        /// <summary>
        /// Generate a GraphQL object type from a SQL table definition, combined with the runtime config entity information
        /// </summary>
        /// <param name="entityName">Name of the entity in the runtime config to generate the GraphQL object type for.</param>
        /// <param name="tableDefinition">SQL table definition information.</param>
        /// <param name="configEntity">Runtime config information for the table.</param>
        /// <returns>A GraphQL object type to be provided to a Hot Chocolate GraphQL document.</returns>
        public static ObjectTypeDefinitionNode FromTableDefinition(string entityName, TableDefinition tableDefinition, [NotNull] Entity configEntity, Dictionary<string, Entity> entities)
        {
            Dictionary<string, FieldDefinitionNode> fields = new();

            foreach ((string columnName, ColumnDefinition column) in tableDefinition.Columns)
            {
                List<DirectiveNode> directives = new();

                if (tableDefinition.PrimaryKey.Contains(columnName))
                {
                    directives.Add(new DirectiveNode(PrimaryKeyDirectiveType.DirectiveName, new ArgumentNode("databaseType", column.SystemType.Name)));
                }

                if (column.IsAutoGenerated)
                {
                    directives.Add(new DirectiveNode(AutoGeneratedDirectiveType.DirectiveName));
                }

                if (column.DefaultValue is not null)
                {
                    IValueNode arg = column.DefaultValue switch
                    {
                        byte value => new ObjectValueNode(new ObjectFieldNode("byte", new IntValueNode(value))),
                        short value => new ObjectValueNode(new ObjectFieldNode("short", new IntValueNode(value))),
                        int value => new ObjectValueNode(new ObjectFieldNode("int", value)),
                        long value => new ObjectValueNode(new ObjectFieldNode("long", new IntValueNode(value))),
                        string value => new ObjectValueNode(new ObjectFieldNode("string", value)),
                        bool value => new ObjectValueNode(new ObjectFieldNode("boolean", value)),
                        float value => new ObjectValueNode(new ObjectFieldNode("float", value)),
                        _ => throw new DataGatewayException($"The type {column.DefaultValue.GetType()} is not supported as a GraphQL default value", HttpStatusCode.InternalServerError, DataGatewayException.SubStatusCodes.GraphQLMapping)
                    };

                    directives.Add(new DirectiveNode(DefaultValueDirectiveType.DirectiveName, new ArgumentNode("value", arg)));
                }

                NamedTypeNode fieldType = new(GetGraphQLTypeForColumnType(column.SystemType));
                FieldDefinitionNode field = new(
                    location: null,
                    new(FormatNameForField(columnName)),
                    description: null,
                    new List<InputValueDefinitionNode>(),
                    column.IsNullable ? fieldType : new NonNullTypeNode(fieldType),
                    directives);

                fields.Add(columnName, field);
            }

            if (configEntity.Relationships is not null)
            {
                foreach ((string relationshipName, Relationship relationship) in configEntity.Relationships)
                {
                    // Generate the field that represents the relationship to ObjectType, so you can navigate through it
                    // and walk the graph
                    string targetEntityName = relationship.TargetEntity.Split('.').Last();
                    Entity referencedEntity = entities[targetEntityName];

                    INullableTypeNode targetField = relationship.Cardinality switch
                    {
                        Cardinality.One =>
                            new NamedTypeNode(FormatNameForObject(targetEntityName, referencedEntity)),
                        Cardinality.Many =>
                            new NamedTypeNode(QueryBuilder.GeneratePaginationTypeName(FormatNameForObject(targetEntityName, referencedEntity))),
                        _ =>
                            throw new DataGatewayException("Specified cardinality isn't supported", HttpStatusCode.InternalServerError, DataGatewayException.SubStatusCodes.GraphQLMapping),
                    };

                    FieldDefinitionNode relationshipField = new(
                        location: null,
                        new NameNode(FormatNameForField(relationshipName)),
                        description: null,
                        new List<InputValueDefinitionNode>(),
                        // TODO: Check for whether it should be a nullable relationship based on the relationship fields
                        new NonNullTypeNode(targetField),
                        new List<DirectiveNode> {
                            new(RelationshipDirectiveType.DirectiveName,
                                new ArgumentNode("target", FormatNameForObject(targetEntityName, referencedEntity)),
                                new ArgumentNode("cardinality", relationship.Cardinality.ToString()))
                        });

                    fields.Add(relationshipField.Name.Value, relationshipField);
                }
            }

            return new ObjectTypeDefinitionNode(
                location: null,
                new(FormatNameForObject(entityName, configEntity)),
                description: null,
                new List<DirectiveNode>() { new(ModelDirectiveType.DirectiveName, new ArgumentNode("name", entityName)) },
                new List<NamedTypeNode>(),
                fields.Values.ToImmutableList());
        }

        /// <summary>
        /// Get the GraphQL type equivalent from ColumnType
        /// </summary>
        public static string GetGraphQLTypeForColumnType(Type type)
        {
            return Type.GetTypeCode(type) switch
            {
                TypeCode.String => "String",
                TypeCode.Byte => "Byte",
                TypeCode.Int16 => "Short",
                TypeCode.Int32 => "Int",
                TypeCode.Int64 => "Long",
                TypeCode.Double => "Float",
                TypeCode.Boolean => "Boolean",
                _ => throw new DataGatewayException(
                        $"Column type {type} not handled by case. Please add a case resolving {type} to the appropriate GraphQL type",
                        HttpStatusCode.InternalServerError,
                        DataGatewayException.SubStatusCodes.GraphQLMapping)
            };
        }
    }
}
