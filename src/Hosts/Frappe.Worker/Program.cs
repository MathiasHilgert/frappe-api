using Frappe.Health;
using Frappe.Worker;

if (ProbeCommand.IsProbe(args, out var probe))
{
    return await ProbeCommand.RunAsync(probe);
}

// The settings files sit next to the binary; the working directory may be anything (in the image it is /app).
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.AddFrappeWorker();

await builder.Build().RunAsync();
return 0;
