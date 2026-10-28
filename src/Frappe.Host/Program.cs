using Frappe.Platform;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddFrappePlatform();

// Every module the host runs, listed explicitly; none is scanned for.
builder.AddFrappeModules();

WebApplication app = builder.Build();
app.MapFrappeEndpoints();

await app.RunAsync();
