using Frappe.Platform.Localization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Frappe.Platform.Web.Tests;

/// <summary>Runs the localization middleware in front of a terminal handler, without a server.</summary>
internal sealed class ApplicationBuilderStub(IServiceProvider services)
{
    public RequestDelegate Build(RequestDelegate terminal)
    {
        var app = new ApplicationBuilder(services);
        app.UsePlatformLocalization();
        app.Run(terminal);
        return app.Build();
    }
}
