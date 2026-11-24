using Frappe.Identity.Infrastructure.Codes;
using Microsoft.Extensions.Options;

namespace Frappe.Identity.UnitTests.Codes;

/// <summary>The doubles and values the code tests share.</summary>
internal static class CodeFixtures
{
    /// <summary>A key of 36 bytes (base64): the text "local-only-code-hash-key-of-36-bytes!".</summary>
    public const string Key = "bG9jYWwtb25seS1jb2RlLWhhc2gta2V5LW9mLTM2LWJ5dGVzIQ==";

    public static readonly Guid Principal = Guid.Parse("0198f2a0-0000-7000-8000-000000000001");

    public static HmacCodeHasher Hasher() => new(Options.Create(new CodeHashOptions { Key = Key }));
}
