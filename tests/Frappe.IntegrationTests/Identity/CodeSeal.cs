using Alba;
using Frappe.Application.Abstractions.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.IntegrationTests.Identity;

/// <summary>Opens a sealed code from a captured code message, as the mailer does, so a test can type it.</summary>
internal static class CodeSeal
{
    public static string Open(IAlbaHost host, string sealedCode) =>
        host.Services.GetRequiredService<IMessageSeal>().Open(sealedCode);
}
