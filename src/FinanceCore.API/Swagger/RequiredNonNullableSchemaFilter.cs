using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace FinanceCore.API.Swagger;

/// <summary>
/// Marca como `required` toda propiedad no-nullable de un schema. Sin esto,
/// Swashbuckle deja todas las propiedades como opcionales en el OpenAPI
/// (porque JSON tolera ausencia), incluso cuando el tipo C# es no-nullable.
///
/// Consecuencia: los tipos TypeScript generados con openapi-typescript
/// quedan con `?` en todas las propiedades, lo que obliga a `data.foo!` o
/// `data.foo ?? default` en cada acceso desde el frontend, perdiendo gran
/// parte del valor del codegen.
///
/// Aplicando este filter, las propiedades que el backend declara como
/// non-nullable C# (gracias al #nullable enable de los proyectos) se marcan
/// como required en el OpenAPI y los tipos TypeScript se vuelven estrictos.
/// </summary>
public class RequiredNonNullableSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema.Properties is null)
            return;

        foreach (var property in schema.Properties)
        {
            if (!property.Value.Nullable)
            {
                schema.Required ??= new HashSet<string>();
                schema.Required.Add(property.Key);
            }
        }
    }
}
