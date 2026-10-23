using Frappe.Api;
using Frappe.Identity.Infrastructure;
using Frappe.Modularity;
using Frappe.Platform.Web.Correlation;
using JasperFx;

var builder = WebApplication.CreateBuilder(args);

// Fail at startup, in every environment, on missing registrations or captive dependencies.
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateOnBuild = true;
    options.ValidateScopes = true;
});

IModule[] modules = [new IdentityModule()];

builder.AddFrappePlatform(modules);

var app = builder.Build();

app.UseRequestCorrelation();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapFrappePlatform(modules);

// No arguments runs the API. Outside Development nothing is created at startup: the deploy runs
// `dotnet Frappe.Api.dll resources setup` first to create the Wolverine message storage schema.
return await app.RunJasperFxCommands(args);

/// <summary>Exposed for integration tests.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1118:Utility classes should not have public constructors",
    Justification = "Test hosts use Program as a generic type argument, so it cannot be static.")]
public partial class Program;
