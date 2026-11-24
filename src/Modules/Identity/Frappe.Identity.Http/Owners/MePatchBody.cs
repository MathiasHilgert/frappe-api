using System.Text.Json;
using Frappe.Identity.Application.Owners.UpdateMe;
using Microsoft.AspNetCore.Http;

namespace Frappe.Identity.Http.Owners;

/// <summary>
/// Reads the body of <c>PATCH /me</c> as a JSON Merge Patch (RFC 7396, HTTP-10). The patch needs what a bound record cannot say, a member that
/// is absent against one that is <c>null</c>, so it is read here. A body that is not a JSON object, a member the profile does not have, a member
/// sent twice and a member of the wrong type are all a 400 before anything is looked at, so a patch is applied whole or not at all.
/// </summary>
internal sealed record MePatchBody(Patched<string>? Given, Patched<string>? Family, Patched<string>? Locale)
{
    private const string NameMember = "name";
    private const string GivenMember = "given";
    private const string FamilyMember = "family";
    private const string LocaleMember = "locale";

    public static async Task<MePatchBody> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            throw Refused("The body is not valid JSON.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Refused("The patch must be a JSON object.");
            }

            Patched<string>? given = null, family = null, locale = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in root.EnumerateObject())
            {
                if (!seen.Add(member.Name))
                {
                    throw Refused($"The member '{member.Name}' is sent twice.");
                }

                switch (member.Name)
                {
                    case NameMember:
                        (given, family) = ReadName(member.Value);
                        break;
                    case LocaleMember:
                        locale = TextOf(member.Value, LocaleMember);
                        break;
                    default:
                        throw Refused($"The profile has no member '{member.Name}'.");
                }
            }

            return new MePatchBody(given, family, locale);
        }
    }

    private static (Patched<string>? Given, Patched<string>? Family) ReadName(JsonElement name)
    {
        // A name sent as null removes it, which the use case refuses: the given name is required.
        if (name.ValueKind == JsonValueKind.Null)
        {
            return (new Patched<string>(null), new Patched<string>(null));
        }

        if (name.ValueKind != JsonValueKind.Object)
        {
            throw Refused("The member 'name' must be an object.");
        }

        Patched<string>? given = null, family = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in name.EnumerateObject())
        {
            if (!seen.Add(part.Name))
            {
                throw Refused($"The member 'name.{part.Name}' is sent twice.");
            }

            switch (part.Name)
            {
                case GivenMember:
                    given = TextOf(part.Value, $"{NameMember}.{GivenMember}");
                    break;
                case FamilyMember:
                    family = TextOf(part.Value, $"{NameMember}.{FamilyMember}");
                    break;
                default:
                    throw Refused($"The name has no member '{part.Name}'.");
            }
        }

        return (given, family);
    }

    private static Patched<string> TextOf(JsonElement value, string member) => value.ValueKind switch
    {
        JsonValueKind.Null => new Patched<string>(null),
        JsonValueKind.String => new Patched<string>(value.GetString()),
        _ => throw Refused($"The member '{member}' must be text or null."),
    };

    private static BadHttpRequestException Refused(string reason) => new(reason, StatusCodes.Status400BadRequest);
}
