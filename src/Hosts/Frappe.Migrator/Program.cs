using Frappe.Migrator;
using Microsoft.Extensions.Hosting;

// The settings files sit next to the binary; the working directory may be anything (in the image it is /app).
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.AddFrappeMigrator();

using var host = builder.Build();
return await host.RunMigrationAsync();
