using CPCREDO.WebApi.Filters;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CPCREDO.WebApi.Swagger;

public sealed class IdempotencyHeaderOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var required = context.MethodInfo
            .GetCustomAttributes(true)
            .OfType<RequiresIdempotencyKeyAttribute>()
            .Any();

        if (!required)
            return;

        operation.Parameters ??= new List<OpenApiParameter>();
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "Idempotency-Key",
            In = ParameterLocation.Header,
            Required = true,
            Description = "Clé unique par opération monétaire. Une répétition avec le même corps renvoie la réponse d’origine.",
            Schema = new OpenApiSchema { Type = "string", Format = "uuid" }
        });
    }
}
