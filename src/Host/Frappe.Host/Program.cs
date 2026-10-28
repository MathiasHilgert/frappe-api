using Frappe.Platform;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddFrappePlatform();

WebApplication app = builder.Build();
app.MapFrappeEndpoints();

await app.RunAsync();
