using Frappe.Migrator;
using Frappe.Observability.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The settings files sit next to the binary; the working directory may be anything (in the image it is /app).
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.AddFrappeTelemetry("frappe-migrator");
using var host = builder.Build();

// Started so telemetry exports; stopped (flushing it) before the exit code is returned.
await host.StartAsync();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Frappe.Migrator");
var stopping = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;

try
{
    await DatabaseMigration.RunAsync(builder.Configuration, logger, stopping);
    await host.StopAsync();
    return 0;
}
catch (Exception exception)
{
    logger.LogCritical(exception, "Database migration failed");
    await host.StopAsync();
    return 1;
}
