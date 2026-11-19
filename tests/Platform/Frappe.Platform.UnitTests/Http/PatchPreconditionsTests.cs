using Frappe.Http;
using Microsoft.AspNetCore.Http;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>HTTP-11: a PATCH is a merge patch, and its optional <c>If-Match</c> is compared strongly (RFC 9110 §13.1.1).</summary>
public sealed class PatchPreconditionsTests
{
    [Test]
    public async Task A_request_without_If_Match_is_unconditional()
    {
        await Assert.That(IfMatch.Read(new DefaultHttpContext().Request)).IsNull();
    }

    [Test]
    [Arguments("\"a\"", new[] { "a" })]
    [Arguments("\"a\", W/\"b\", \"c\"", new[] { "a", "c" })]
    [Arguments("*", new[] { IfMatch.Any })]
    [Arguments("W/\"b\"", new string[0])]
    [Arguments("not a tag", new string[0])]
    public async Task If_Match_keeps_its_strong_tags_unquoted_and_reads_a_malformed_header_as_accepting_none(string header, string[] expected)
    {
        var request = new DefaultHttpContext().Request;
        request.Headers.IfMatch = header;

        await Assert.That(IfMatch.Read(request)).IsEquivalentTo(expected);
    }

    [Test]
    [Arguments("application/merge-patch+json", true)]
    [Arguments("Application/Merge-Patch+JSON; charset=utf-8", true)]
    [Arguments("application/json", false)]
    [Arguments(null, false)]
    public async Task A_merge_patch_body_is_told_by_its_media_type_whatever_its_case_and_parameters(string? contentType, bool expected)
    {
        var request = new DefaultHttpContext().Request;
        request.ContentType = contentType;

        await Assert.That(MergePatch.IsBodyOf(request)).IsEqualTo(expected);
    }
}
