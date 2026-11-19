using Frappe.Migrator;
using Microsoft.Extensions.Hosting;

// The content root is pinned to the binary's directory so that settings files never depend on the working directory.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.AddFrappeMigrator();

using var host = builder.Build();
return await host.RunMigrationAsync();
