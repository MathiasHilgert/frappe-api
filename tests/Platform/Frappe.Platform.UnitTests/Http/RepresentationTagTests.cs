using System.Security.Cryptography;
using System.Text.Json;
using Frappe.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>HTTP-12: the tag of a representation is the SHA-256 of its languages, a newline and its bytes, cut to 16 bytes.</summary>
public sealed class RepresentationTagTests
{
    [Test]
    public async Task The_tag_is_the_quoted_base64url_of_the_first_16_hash_bytes_of_the_language_a_newline_and_the_body()
    {
        var body = "{\"a\":1}"u8.ToArray();
        var hash = SHA256.HashData([.. "en, es\n"u8, .. body]);
        var expected = "\"" + Convert.ToBase64String(hash, 0, 16).TrimEnd('=').Replace('+', '-').Replace('/', '_') + "\"";

        await Assert.That(RepresentationTag.Of("en, es", body)).IsEqualTo(expected);
    }

    [Test]
    public async Task Two_language_variants_of_one_body_have_two_tags()
    {
        var body = "{}"u8.ToArray();

        await Assert.That(RepresentationTag.Of("en", body)).IsNotEqualTo(RepresentationTag.Of("es", body));
    }

    [Test]
    public async Task A_response_is_tagged_over_the_bytes_the_host_JSON_options_write_and_its_Content_Language()
    {
        var services = new ServiceCollection();
        services.Configure<JsonOptions>(options => options.SerializerOptions.WriteIndented = true);
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        httpContext.Response.Headers.ContentLanguage = "es";
        var response = new Sample("x", 1);
        var written = JsonSerializer.SerializeToUtf8Bytes(response, Indented);

        await Assert.That(RepresentationTag.Of(httpContext, response)).IsEqualTo(RepresentationTag.Of("es", written));
    }

    private static readonly JsonSerializerOptions Indented = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>A response with two members, so its serialized bytes depend on the JSON options.</summary>
    private sealed record Sample(string Name, int Count);
}
