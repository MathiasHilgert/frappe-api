using System.Globalization;
using Frappe.Http;
using Frappe.Identity.Application.Owners;
using Frappe.Identity.Application.Owners.GetMe;
using Frappe.Identity.Application.Owners.UpdateMe;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;

namespace Frappe.Identity.Http.Owners;

/// <summary>
/// The owner's own profile (S10, HTTP-08): <c>GET /me</c> reads it and <c>PATCH /me</c> changes the name and the language with a JSON Merge Patch.
/// Both ask for an owner's session and take the owner from it, never from the URL. Answers are never cached; they carry a strong <c>ETag</c> that
/// names the version of the profile (not a cache validator), which <c>If-Match</c> may quote to refuse a change over a newer one (HTTP-11, HTTP-12).
/// </summary>
internal static class MeEndpoints
{
    extension(IEndpointRouteBuilder routes)
    {
        public void MapMeEndpoints()
        {
            routes.MapGet("/me", GetAsync)
                .WithName("GetMe").WithTags(IdentityHttp.Tag).NoStore().RequireOwner()
                .SendsNoStoreETag(ETagDescription, NoStoreDescription)
                .WithSummary("Get the owner's profile")
                .WithDescription(
                    "The profile of the owner the session belongs to: the id, the kind, the name as typed, the address and the language of the mail "
                    + "the owner receives (its BCP 47 code and its own name, whatever Accept-Language says). 401 identity.session.invalid without "
                    + "a valid session; 403 identity.session.forbidden when the session is not an owner's.")
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);

            routes.MapPatch("/me", PatchAsync)
                .WithName("UpdateMe").WithTags(IdentityHttp.Tag).NoStore().RequireOwner()
                .Accepts<MeMergePatch>(MergePatch.MediaType)
                .DeclaresAcceptPatch("the profile")
                .DeclaresIfMatch("An ETag of GET /me or of an earlier answer of this route; when it is no longer the profile's, nothing changes and "
                    + "the answer is 412 identity.profile.changed. Without it the change is made over whatever the profile is.")
                .SendsNoStoreETag(ETagDescription, NoStoreDescription)
                .WithSummary("Change the owner's profile")
                .WithDescription(
                    "Changes the name and the language with a JSON Merge Patch (application/merge-patch+json): a member left out stays as it is, "
                    + "name.family may be null to remove it, name.given and locale may not. Every member is checked before anything changes. "
                    + $"The name, given plus family, is at most {Domain.Owners.OwnerName.MaxMailedLength} characters and holds no http, www. or "
                    + "://: 400 identity.name.invalid. The language is a plain BCP 47 tag: 400 for one that is malformed, 422 "
                    + "identity.locale.unsupported for one Frappe is not written in. A member the profile does not have, a member sent twice and a "
                    + "member of the wrong type are 400 http.bad_request. It answers 200 with the whole profile and its new ETag; 401 and 403 as "
                    + "GET /me.")
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status412PreconditionFailed)
                .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
                .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        }
    }

    private const string KindFamily = "identity.kind";

    private const string ETagDescription =
        "A strong tag that names this version of the profile; it changes whenever the name or the language does. Send it as If-Match to PATCH /me.";

    private const string NoStoreDescription = "no-store: the profile is personal and is never cached.";

    private static async Task<Results<Ok<MeResponse>, ErrorsProblemHttpResult>> GetAsync(
        HttpContext httpContext, IQueryHandler<GetMe, OwnerProfile> handler, CancellationToken cancellationToken)
    {
        var principal = SessionPolicy.RequiredPrincipalIdOf(httpContext.User);
        return (await handler.HandleAsync(new GetMe(principal), cancellationToken)).ToTypedHttpResult(profile => Answer(httpContext, profile));
    }

    private static async Task<Results<Ok<MeResponse>, ErrorsProblemHttpResult>> PatchAsync(
        HttpContext httpContext,
        ICommandHandler<UpdateMe, Unit> handler,
        IQueryHandler<GetMe, OwnerProfile> reader,
        CancellationToken cancellationToken)
    {
        var principal = SessionPolicy.RequiredPrincipalIdOf(httpContext.User);
        var patch = await MePatchBody.ReadAsync(httpContext.Request, cancellationToken);
        var command = new UpdateMe(principal, patch.Given, patch.Family, patch.Locale, IfMatch.Read(httpContext.Request));
        var updated = await handler.HandleAsync(command, cancellationToken);
        if (!updated.IsSuccess)
        {
            return Result.Failure<OwnerProfile>(updated.Errors).ToTypedHttpResult(profile => Answer(httpContext, profile));
        }

        // The version the change got exists only once it has committed, so the answer is read back: its ETag is the one the next If-Match needs.
        return (await reader.HandleAsync(new GetMe(principal), cancellationToken)).ToTypedHttpResult(profile => Answer(httpContext, profile));
    }

    private static Ok<MeResponse> Answer(HttpContext httpContext, OwnerProfile profile)
    {
        var response = new MeResponse(
            profile.Id,
            EnumeratedValues.Of(httpContext, KindFamily, profile.Kind),
            new OwnerNameResponse(profile.GivenName, profile.FamilyName),
            profile.Email,
            new EnumeratedValue(profile.Locale, NativeNameOf(profile.Locale)));
        httpContext.Response.Headers[HeaderNames.ETag] = $"\"{profile.Version}\"";
        return TypedResults.Ok(response);
    }

    /// <summary>A language names itself (HTTP-25): the same whatever the request language.</summary>
    private static string NativeNameOf(string tag)
    {
        try
        {
            return CultureInfo.GetCultureInfo(tag).NativeName;
        }
        catch (CultureNotFoundException)
        {
            return tag;
        }
    }
}
