using System.Security.Cryptography;
using Frappe.Identity.Application.Security;
using Microsoft.AspNetCore.WebUtilities;

namespace Frappe.IntegrationTests.Identity;

/// <summary>Random session tokens, but a known verification code so a test can type what the owner would read.</summary>
internal sealed class FixedCodeGenerator : ISecretGenerator
{
    public const string Code = "123456";

    public string NewSessionToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public string NewVerificationCode() => Code;
}
