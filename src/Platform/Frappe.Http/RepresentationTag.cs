using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Frappe.Http;

/// <summary>
/// The strong entity tag of a JSON representation (RFC 9110 §8.8.3): it hashes the bytes sent and the languages they are
/// written in, so two language variants never share one.
/// </summary>
public static class RepresentationTag
{
    /// <summary>Bytes of SHA-256 kept in the tag: collisions stay negligible for a validator.</summary>
    private const int TagBytes = 16;

    /// <summary>The quoted strong tag of <paramref name="body"/> written in <paramref name="contentLanguage"/> (the response's
    /// <c>Content-Language</c>).</summary>
    public static string Of(StringValues contentLanguage, ReadOnlySpan<byte> body)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(contentLanguage.ToString() + "\n"));
        hash.AppendData(body);
        return "\"" + Base64Url.EncodeToString(hash.GetHashAndReset().AsSpan(0, TagBytes)) + "\"";
    }

    /// <summary>
    /// The quoted strong tag of <paramref name="response"/> as the result executor writes it: serialized with the host's HTTP
    /// JSON options, in the response's <c>Content-Language</c>. Set <c>Content-Language</c> before calling it.
    /// </summary>
    public static string Of<T>(HttpContext httpContext, T response)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var options = httpContext.RequestServices.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        return Of(httpContext.Response.Headers.ContentLanguage, JsonSerializer.SerializeToUtf8Bytes(response, options));
    }
}
