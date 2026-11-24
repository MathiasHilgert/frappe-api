using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Frappe.Identity.Application.Owners;
using Frappe.Identity.Application.Owners.GetMe;
using Frappe.Identity.Application.Owners.UpdateMe;
using Frappe.Identity.Application.Sessions.AuthenticateSession;
using Frappe.Modules;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;

namespace Frappe.Api.IntegrationTests;

/// <summary>
/// The owner's profile routes (S10) over the real session authentication with the profile use cases faked: an owner-only policy, the profile with its
/// <c>{code, name}</c> values and its version as a strong <c>ETag</c>, and the merge patch read member by member (absent against null, unknown and
/// repeated members refused) with <c>If-Match</c> handed to the use case and the new version read back.
/// </summary>
public sealed class IdentityMeRouteTests
{
    private const string Me = "/v1/identity/me";

    private static HttpRequestMessage Request(
        HttpMethod method, string token, string? body = null, string? contentType = null, string? ifMatch = null, string? language = null)
    {
        var request = new HttpRequestMessage(method, Me);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        if (language is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", language);
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, new MediaTypeHeaderValue(contentType ?? "application/merge-patch+json"));
        }

        return request;
    }

    private static HttpRequestMessage Patch(string token, string body, string? ifMatch = null) =>
        Request(HttpMethod.Patch, token, body, ifMatch: ifMatch);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    [Test]
    public async Task The_profile_is_the_owners_with_translated_kind_a_self_named_language_and_the_version_as_a_strong_etag()
    {
        // Arrange
        var fakes = new Fakes();
        await using var host = await IdentitySessionHost.StartAsync(extra: new FakesModule(fakes));
        var (principal, _, token) = host.Seed();
        fakes.Current = Profile(principal.Id, version: "4242");
        using var client = host.App.GetTestClient();

        // Act
        using var english = await client.SendAsync(Request(HttpMethod.Get, token));
        using var spanish = await client.SendAsync(Request(HttpMethod.Get, token, language: "es"));
        var body = await ReadAsync(english);
        var spanishBody = await ReadAsync(spanish);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(english.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(english.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(english.Headers.ETag?.Tag).IsEqualTo("\"4242\"");
            await Assert.That(english.Headers.ETag?.IsWeak).IsFalse();
            await Assert.That(fakes.Reads).Count().IsEqualTo(2);
            await Assert.That(body.GetProperty("id").GetGuid()).IsEqualTo(principal.Id);
            await Assert.That(body.GetProperty("kind").GetProperty("code").GetString()).IsEqualTo("owner");
            await Assert.That(body.GetProperty("kind").GetProperty("name").GetString()).IsEqualTo("Owner");
            await Assert.That(spanishBody.GetProperty("kind").GetProperty("name").GetString()).IsEqualTo("Titular");
            await Assert.That(body.GetProperty("name").GetProperty("given").GetString()).IsEqualTo("Ana");
            await Assert.That(body.GetProperty("name").GetProperty("family").GetString()).IsEqualTo("Pérez");
            await Assert.That(body.GetProperty("email").GetString()).IsEqualTo("ana@example.com");
            await Assert.That(body.GetProperty("locale").GetProperty("code").GetString()).IsEqualTo("es-AR");
            await Assert.That(body.GetProperty("locale").GetProperty("name").GetString()).IsEqualTo("español (Argentina)");
            await Assert.That(spanishBody.GetProperty("locale").GetProperty("name").GetString()).IsEqualTo("español (Argentina)");
        }
    }

    [Test]
    public async Task A_request_without_a_session_is_the_401_and_a_session_that_is_not_an_owners_is_the_403()
    {
        // Arrange
        var fakes = new Fakes();
        await using var owners = await IdentitySessionHost.StartAsync(extra: new FakesModule(fakes));
        await using var staff = await IdentitySessionHost.StartAsync(extra: new FakesModule(fakes, kind: "staff"));
        var (_, _, staffToken) = staff.Seed();
        using var ownerClient = owners.App.GetTestClient();
        using var staffClient = staff.App.GetTestClient();

        // Act
        using var anonymous = await ownerClient.GetAsync(Me);
        using var anonymousPatch = await ownerClient.SendAsync(new HttpRequestMessage(HttpMethod.Patch, Me));
        using var forbidden = await staffClient.SendAsync(Request(HttpMethod.Get, staffToken));
        using var forbiddenPatch = await staffClient.SendAsync(Patch(staffToken, "{}"));
        var problem = await ReadAsync(forbidden);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            await Assert.That(anonymousPatch.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            await Assert.That(forbidden.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            await Assert.That(forbiddenPatch.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            await Assert.That(problem.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo("identity.session.forbidden");
            await Assert.That(problem.GetProperty("errors")[0].GetProperty("message").GetString()).IsNotEmpty();
            await Assert.That(fakes.Reads.Count + fakes.Updates.Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task A_patch_hands_the_use_case_the_members_it_names_and_If_Match_and_answers_the_profile_read_back_with_its_new_etag()
    {
        // Arrange
        var fakes = new Fakes();
        await using var host = await IdentitySessionHost.StartAsync(extra: new FakesModule(fakes));
        var (principal, _, token) = host.Seed();
        fakes.Current = Profile(principal.Id, version: "1");
        fakes.AfterUpdate = Profile(principal.Id, version: "2", given: "Anabel");
        using var client = host.App.GetTestClient();

        // Act
        using var response = await client.SendAsync(Patch(token, """{"name":{"given":"Anabel","family":null},"locale":"pt-BR"}""", "\"1\""));
        var body = await ReadAsync(response);

        // Assert
        var command = fakes.Updates.Single();
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(response.Headers.ETag?.Tag).IsEqualTo("\"2\"");
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(body.GetProperty("name").GetProperty("given").GetString()).IsEqualTo("Anabel");
            await Assert.That(command.Given).IsEqualTo(new Patched<string>("Anabel"));
            await Assert.That(command.Family).IsEqualTo(new Patched<string>(null));
            await Assert.That(command.Locale).IsEqualTo(new Patched<string>("pt-BR"));
            await Assert.That(command.IfMatch).IsEquivalentTo(["1"]);
        }
    }

    [Test]
    public async Task A_member_left_out_is_absent_and_one_sent_as_null_is_null()
    {
        // Arrange
        var fakes = new Fakes();
        await using var host = await IdentitySessionHost.StartAsync(extra: new FakesModule(fakes));
        var (principal, _, token) = host.Seed();
        fakes.Current = Profile(principal.Id, version: "1");
        using var client = host.App.GetTestClient();

        // Act
        (await client.SendAsync(Patch(token, "{}"))).Dispose();
        (await client.SendAsync(Patch(token, """{"name":{"family":null}}"""))).Dispose();
        (await client.SendAsync(Patch(token, """{"name":null,"locale":null}"""))).Dispose();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(fakes.Updates[0]).IsEqualTo(fakes.Updates[0] with { Given = null, Family = null, Locale = null, IfMatch = null });
            await Assert.That(fakes.Updates[0].IfMatch).IsNull();
            await Assert.That(fakes.Updates[1].Given).IsNull();
            await Assert.That(fakes.Updates[1].Family).IsEqualTo(new Patched<string>(null));
            await Assert.That(fakes.Updates[1].Locale).IsNull();
            await Assert.That(fakes.Updates[2].Given).IsEqualTo(new Patched<string>(null));
            await Assert.That(fakes.Updates[2].Family).IsEqualTo(new Patched<string>(null));
            await Assert.That(fakes.Updates[2].Locale).IsEqualTo(new Patched<string>(null));
        }
    }

    [Test]
    [Arguments("\"7\", \"8\"", new[] { "7", "8" })]
    [Arguments("*", new[] { "*" })]
    [Arguments("W/\"7\"", new string[0])]
    [Arguments("not a tag", new string[0])]
    public async Task If_Match_is_read_strongly_and_a_header_that_cannot_be_read_matches_nothing(string header, string[] expected)
    {
        // Arrange
        var fakes = new Fakes();
        await using var host = await IdentitySessionHost.StartAsync(extra: new FakesModule(fakes));
        var (principal, _, token) = host.Seed();
        fakes.Current = Profile(principal.Id, version: "1");
        using var client = host.App.GetTestClient();

        // Act
        (await client.SendAsync(Patch(token, "{}", header))).Dispose();

        // Assert
        await Assert.That(fakes.Updates.Single().IfMatch).IsEquivalentTo(expected);
    }

    [Test]
    [Arguments("""{"email":"x@example.com"}""")]
    [Arguments("""{"name":{"middle":"X"}}""")]
    [Arguments("""{"locale":"es","locale":"en"}""")]
    [Arguments("""{"name":{"given":"A","given":"B"}}""")]
    [Arguments("""{"locale":5}""")]
    [Arguments("""{"name":"Ana"}""")]
    [Arguments("""["es"]""")]
    [Arguments("""{"locale":""")]
    [Arguments("")]
    public async Task A_patch_with_an_unknown_or_repeated_member_or_a_wrong_type_is_a_400_before_anything_is_changed(string body)
    {
        // Arrange
        var fakes = new Fakes();
        await using var host = await IdentitySessionHost.StartAsync(extra: new FakesModule(fakes));
        var (principal, _, token) = host.Seed();
        fakes.Current = Profile(principal.Id, version: "1");
        using var client = host.App.GetTestClient();

        // Act
        using var response = await client.SendAsync(Patch(token, body));
        var problem = await ReadAsync(response);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(problem.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo("http.bad_request");
            await Assert.That(fakes.Updates.Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task A_patch_that_is_not_a_merge_patch_is_a_415_naming_the_format()
    {
        // Arrange
        var fakes = new Fakes();
        await using var host = await IdentitySessionHost.StartAsync(extra: new FakesModule(fakes));
        var (principal, _, token) = host.Seed();
        fakes.Current = Profile(principal.Id, version: "1");
        using var client = host.App.GetTestClient();

        // Act
        using var response = await client.SendAsync(Request(HttpMethod.Patch, token, "{}", "application/json"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.UnsupportedMediaType);
            await Assert.That(string.Join(", ", response.Headers.GetValues("Accept-Patch"))).IsEqualTo("application/merge-patch+json");
            await Assert.That(fakes.Updates.Count).IsEqualTo(0);
        }
    }

    [Test]
    [Arguments("identity.profile.changed", 412)]
    [Arguments("identity.locale.unsupported", 422)]
    [Arguments("identity.name.invalid", 400)]
    public async Task The_use_cases_refusals_are_answered_with_their_status_code_and_text(string code, int status)
    {
        // Arrange
        var fakes = new Fakes();
        await using var host = await IdentitySessionHost.StartAsync(extra: new FakesModule(fakes));
        var (principal, _, token) = host.Seed();
        fakes.Current = Profile(principal.Id, version: "1");
        fakes.Refusal = code switch
        {
            "identity.profile.changed" => new PreconditionFailedError(code),
            "identity.locale.unsupported" => Error.RuleViolation(code),
            _ => Error.InvalidValue(code),
        };
        using var client = host.App.GetTestClient();

        // Act
        using var response = await client.SendAsync(Patch(token, """{"locale":"fr"}"""));
        var problem = await ReadAsync(response);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That((int)response.StatusCode).IsEqualTo(status);
            await Assert.That(problem.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo(code);
            await Assert.That(problem.GetProperty("errors")[0].GetProperty("message").GetString()).IsNotEmpty();
            await Assert.That(response.Headers.ETag).IsNull();
        }
    }

    [Test]
    public async Task The_contract_documents_the_etag_if_match_and_the_merge_patch()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync(extra: new FakesModule(new Fakes()));
        using var client = host.App.GetTestClient();

        // Act
        using var contract = await client.GetAsync("/openapi/v1.json");
        var document = await ReadAsync(contract);

        // Assert
        var me = document.GetProperty("paths").GetProperty("/v1/identity/me");
        var patch = me.GetProperty("patch");
        using (Assert.Multiple())
        {
            await Assert.That(me.GetProperty("get").GetProperty("responses").GetProperty("200").GetProperty("headers")
                .TryGetProperty("ETag", out _)).IsTrue();
            await Assert.That(patch.GetProperty("requestBody").GetProperty("content").TryGetProperty("application/merge-patch+json", out _)).IsTrue();
            await Assert.That(patch.GetProperty("parameters").EnumerateArray().Any(p => p.GetProperty("name").GetString() == "If-Match")).IsTrue();
            await Assert.That(patch.GetProperty("responses").EnumerateObject().Select(r => r.Name)).Contains("412").And.Contains("422");
        }
    }

    private static OwnerProfile Profile(Guid id, string version, string given = "Ana") =>
        new(id, "owner", given, "Pérez", "ana@example.com", "es-AR", version);

    /// <summary>What the faked use cases saw and the answers they give.</summary>
    private sealed class Fakes
    {
        public List<GetMe> Reads { get; } = [];

        public List<UpdateMe> Updates { get; } = [];

        public OwnerProfile? Current { get; set; }

        public OwnerProfile? AfterUpdate { get; set; }

        public Error? Refusal { get; set; }
    }

    /// <summary>
    /// Registers the faked profile use cases, and, when <paramref name="kind"/> is given, an authentication that answers every token as a principal
    /// of that kind (the enum of principal kinds has no member to seed one with).
    /// </summary>
    private sealed class FakesModule(Fakes fakes, string? kind = null) : IModule
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddFakeQuery<GetMe, OwnerProfile>(new Read(fakes));
            services.AddFakeCommand<UpdateMe, Unit>(new Update(fakes));
            if (kind is not null)
            {
                services.AddFakeQuery<AuthenticateSession, SessionPrincipal>(new Authenticate(kind));
            }
        }
    }

    /// <summary>The profile query, standing in for the real one.</summary>
    private sealed class Read(Fakes fakes) : IQueryHandler<GetMe, OwnerProfile>
    {
        public Task<Result<OwnerProfile>> HandleAsync(GetMe query, CancellationToken cancellationToken)
        {
            fakes.Reads.Add(query);
            var profile = fakes.Updates.Count > 0 && fakes.AfterUpdate is not null ? fakes.AfterUpdate : fakes.Current;
            return Task.FromResult<Result<OwnerProfile>>(profile!);
        }
    }

    /// <summary>The profile command, standing in for the real one.</summary>
    private sealed class Update(Fakes fakes) : ICommandHandler<UpdateMe, Unit>
    {
        public Task<Result<Unit>> HandleAsync(UpdateMe command, CancellationToken cancellationToken)
        {
            fakes.Updates.Add(command);
            return Task.FromResult<Result<Unit>>(fakes.Refusal is null ? Unit.Value : fakes.Refusal);
        }
    }

    /// <summary>An authentication that answers every token as a principal of one kind.</summary>
    private sealed class Authenticate(string kind) : IQueryHandler<AuthenticateSession, SessionPrincipal>
    {
        public Task<Result<SessionPrincipal>> HandleAsync(AuthenticateSession query, CancellationToken cancellationToken) =>
            Task.FromResult<Result<SessionPrincipal>>(new SessionPrincipal(
                Guid.CreateVersion7(), kind, Guid.CreateVersion7(), [SessionPrincipal.PasswordMethod], Instant.FromUtc(2026, 11, 24, 12, 0), null));
    }
}
