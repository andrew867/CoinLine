using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace HostPlatform.Api.Swagger;

/// <summary>Customer-facing OpenAPI branding for CoinLine API (CoinLine Server).</summary>
public sealed class CoinLineOpenApiDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        swaggerDoc.Info.Title = "CoinLine API";
        swaggerDoc.Info.Version = "v1";
        swaggerDoc.Info.Description =
            "REST API for the CoinLine Payphone Management Platform (CoinLine Server component). " +
            "Provides HTTPS contracts for fleet provisioning, table distribution, call rating, card and account administration, technician craft operations, firmware package management, health probes, and audit logging. " +
            "Terminate TLS at your edge; authenticate with API keys as described in product documentation.";
    }
}

/// <summary>Maps ASP.NET controller names to customer-facing OpenAPI tag groups.</summary>
public sealed class CoinLineTagOperationFilter : IOperationFilter
{
    private static readonly Dictionary<string, string> ControllerToTag = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Customers"] = "Customers",
        ["Sites"] = "Sites",
        ["Terminals"] = "Terminals",
        ["NccSessions"] = "Terminals",
        ["NccFrameCaptures"] = "Terminals",
        ["Dlog"] = "Diagnostics",
        ["Tables"] = "Table Distribution",
        ["Downloads"] = "Downloads",
        ["Uploads"] = "Uploads",
        ["RatingOperations"] = "Rating",
        ["RatePlans"] = "Rating",
        ["RateRules"] = "Rating",
        ["NumberClasses"] = "Rating",
        ["CallRecords"] = "Rating",
        ["Cards"] = "Cards",
        ["Smartcards"] = "Cards",
        ["Craft"] = "Craft Operations",
        ["Firmware"] = "Firmware Packages",
        ["Operator"] = "Operations",
        ["Audit"] = "Audit",
        ["HardwareValidation"] = "Field Validation"
    };

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.ApiDescription.ActionDescriptor is not ControllerActionDescriptor cad)
            return;
        if (!ControllerToTag.TryGetValue(cad.ControllerName, out var tagName))
            return;
        operation.Tags ??= new List<OpenApiTag>();
        operation.Tags.Clear();
        operation.Tags.Add(new OpenApiTag { Name = tagName });
    }
}
