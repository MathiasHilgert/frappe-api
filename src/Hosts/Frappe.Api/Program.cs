using Frappe.Api;
using Frappe.Health;

if (ProbeCommand.IsProbe(args, out var probe))
{
    return await ProbeCommand.RunAsync(probe);
}

// The settings files sit next to the binary; the working directory may be anything (in the image it is /app).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.AddFrappeApi();

var app = builder.Build();
app.MapFrappeApi();
await app.RunAsync();
return 0;
