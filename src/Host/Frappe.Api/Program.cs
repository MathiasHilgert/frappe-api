using Frappe.Api;
using JasperFx;

var builder = WebApplication.CreateBuilder(args);
builder.AddFrappe(Modules.All);

var app = builder.Build();
app.UseFrappe();
app.MapFrappe(Modules.All);

// No arguments runs the API. Outside Development nothing is created at startup: the deploy runs
// `dotnet Frappe.Api.dll resources setup` first to create the Wolverine and Quartz storage schemas.
return await app.RunJasperFxCommands(args);
