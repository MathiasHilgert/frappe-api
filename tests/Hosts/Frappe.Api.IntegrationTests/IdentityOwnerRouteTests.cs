using System.Net;
using System.Text.Json;
using Frappe.Http;
using Frappe.Identity.Application.Owners.RegisterOwner;
using Frappe.Identity.Application.Owners.ResendOwnerVerification;
using Frappe.Identity.Application.Owners.VerifyOwnerEmail;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Application.Sessions.GetCurrentSession;
using Frappe.Identity.Application.Sessions.LogOut;
using Frappe.Identity.Http;
using Frappe.Identity.Http.Sessions;
using Frappe.Modules;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;

namespace Frappe.Api.IntegrationTests;

/// <summary>
/// The owner routes (S3, S6) over the real Api with the use cases faked: what each route hands the use case (the language negotiated from
/// <c>Accept-Language</c>, the id as text), the status each answers, no <c>Location</c> and no cache, the session cookie on verification, and the
/// use case's refusals answered as Problems with their codes and texts.
/// </summary>
public sealed class IdentityOwnerRouteTests
{
    private static readonly Guid OwnerId = Guid.Parse("0198f2a0-0000-7000-8000-0000000000aa");

    private static readonly object Registration = new
    {
        name = new { given = "Ana", family = "Pérez" },
        email = "ana@example.com",
        password = "correct horse battery",
    };

    private static Task<WebApplication> StartAsync(Fakes fakes) => ApiHost.StartAsync([new FakesModule(fakes)], [new IdentityHttp()]);

    private static HttpRequestMessage Post(string path, object? body = null, string? language = null, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = body is null ? null : JsonContent.Create(body) };
        if (language is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", language);
        }

        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return request;
    }

    [Test]
    public async Task Registering_answers_201_with_the_id_and_no_location_and_hands_the_use_case_the_negotiated_language()
    {
        // Arrange
        var fakes = new Fakes();
        await using var app = await StartAsync(fakes);
        using var client = app.GetTestClient();

        // Act
        using var response = await client.SendAsync(Post("/v1/identity/owners", Registration, "fr, pt-BR;q=0.8", "key-1"));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        var command = fakes.Registrations.Single();
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
            await Assert.That(response.Headers.Location).IsNull();
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(body.RootElement.GetProperty("id").GetGuid()).IsEqualTo(OwnerId);
            await Assert.That(command.GivenName).IsEqualTo("Ana");
            await Assert.That(command.FamilyName).IsEqualTo("Pérez");
            await Assert.That(command.Email).IsEqualTo("ana@example.com");
            await Assert.That(command.Password).IsEqualTo("correct horse battery");
            await Assert.That(command.Locale).IsEqualTo("pt-BR");
        }
    }

    [Test]
    public async Task A_language_nobody_serves_is_resolved_to_english_and_never_refused()
    {
        // Arrange
        var fakes = new Fakes();
        await using var app = await StartAsync(fakes);
        using var client = app.GetTestClient();

        // Act
        using var response = await client.SendAsync(Post("/v1/identity/owners", Registration, "xx-YY"));
        using var withoutHeader = await client.SendAsync(Post("/v1/identity/owners", Registration));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
            await Assert.That(withoutHeader.StatusCode).IsEqualTo(HttpStatusCode.Created);
            await Assert.That(fakes.Registrations.Select(c => c.Locale!)).IsEquivalentTo(["en", "en"]);
        }
    }

    [Test]
    public async Task A_registration_without_a_body_reaches_the_use_case_to_be_refused_there()
    {
        // Arrange
        var fakes = new Fakes { Registered = Error.InvalidValue("identity.email.invalid") };
        await using var app = await StartAsync(fakes);
        using var client = app.GetTestClient();

        // Act
        using var response = await client.SendAsync(Post("/v1/identity/owners", new { }));
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        var error = problem.RootElement.GetProperty("errors")[0];
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(error.GetProperty("code").GetString()).IsEqualTo("identity.email.invalid");
            await Assert.That(error.GetProperty("message").GetString()).IsNotEmpty();
            await Assert.That(fakes.Registrations.Single().Email).IsNull();
        }
    }

    [Test]
    public async Task Verifying_answers_201_with_the_session_in_the_body_and_the_cookie_and_no_location()
    {
        // Arrange
        var fakes = new Fakes();
        await using var app = await StartAsync(fakes);
        using var client = app.GetTestClient();

        // Act
        using var response = await client.SendAsync(Post($"/v1/identity/owners/{OwnerId}/verification", new { code = "482913" }));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        var command = fakes.Verifications.Single();
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
            await Assert.That(response.Headers.Location).IsNull();
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(body.RootElement.GetProperty("token").GetString()).IsEqualTo(fakes.Token);
            await Assert.That(body.RootElement.GetProperty("id").GetGuid()).IsEqualTo(fakes.SessionId);
            await Assert.That(body.RootElement.GetProperty("idleExpiresAt").GetDateTimeOffset())
                .IsEqualTo(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero));
            await Assert.That(cookie).StartsWith($"{IdentitySessionCookie.Name}={fakes.Token}").And.Contains("httponly").And.Contains("secure");
            await Assert.That(command.OwnerId).IsEqualTo(OwnerId.ToString());
            await Assert.That(command.Code).IsEqualTo("482913");
        }
    }

    [Test]
    public async Task A_malformed_id_reaches_the_use_case_as_text_and_its_refusal_is_the_422_with_the_code_message()
    {
        // Arrange
        var fakes = new Fakes { Verified = Error.RuleViolation("identity.code.invalid") };
        await using var app = await StartAsync(fakes);
        using var client = app.GetTestClient();

        // Act
        using var response = await client.SendAsync(Post("/v1/identity/owners/not-a-guid/verification", new { code = "482913" }));
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        var error = problem.RootElement.GetProperty("errors")[0];
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.UnprocessableEntity);
            await Assert.That(error.GetProperty("code").GetString()).IsEqualTo("identity.code.invalid");
            await Assert.That(error.GetProperty("message").GetString()).IsNotEmpty();
            await Assert.That(response.Headers.Contains("Set-Cookie")).IsFalse();
            await Assert.That(fakes.Verifications.Single().OwnerId).IsEqualTo("not-a-guid");
        }
    }

    [Test]
    public async Task Asking_for_a_new_code_answers_202_with_no_location_whatever_the_id()
    {
        // Arrange
        var fakes = new Fakes();
        await using var app = await StartAsync(fakes);
        using var client = app.GetTestClient();

        // Act
        using var known = await client.SendAsync(Post($"/v1/identity/owners/{OwnerId}/verification-codes"));
        using var malformed = await client.SendAsync(Post("/v1/identity/owners/nope/verification-codes"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(known.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
            await Assert.That(malformed.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
            await Assert.That(known.Headers.Location).IsNull();
            await Assert.That(known.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(fakes.Resends.Select(c => c.OwnerId!)).IsEquivalentTo([OwnerId.ToString(), "nope"]);
        }
    }

    [Test]
    public async Task The_contract_lists_the_three_routes_without_a_location_header()
    {
        // Arrange
        await using var app = await StartAsync(new Fakes());
        using var client = app.GetTestClient();

        // Act
        using var contract = await client.GetAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(await contract.Content.ReadAsStringAsync());

        // Assert
        var paths = document.RootElement.GetProperty("paths");
        var register = paths.GetProperty("/v1/identity/owners").GetProperty("post").GetProperty("responses").GetProperty("201");
        using (Assert.Multiple())
        {
            await Assert.That(register.TryGetProperty("headers", out var headers) && headers.TryGetProperty("Location", out _)).IsFalse();
            await Assert.That(paths.TryGetProperty("/v1/identity/owners/{ownerId}/verification", out _)).IsTrue();
            await Assert.That(paths.TryGetProperty("/v1/identity/owners/{ownerId}/verification-codes", out _)).IsTrue();
        }
    }

    /// <summary>What the faked use cases saw and the answers they give.</summary>
    private sealed class Fakes
    {
        public List<RegisterOwner> Registrations { get; } = [];

        public List<VerifyOwnerEmail> Verifications { get; } = [];

        public List<ResendOwnerVerification> Resends { get; } = [];

        public Error? Registered { get; init; }

        public Error? Verified { get; init; }

        public Guid SessionId { get; } = Guid.CreateVersion7();

        public string Token { get; } = new Frappe.Identity.Infrastructure.Sessions.SessionTokens().Issue().Value;
    }

    /// <summary>Registers the fakes, and an idempotency store that never replays so no Valkey is needed.</summary>
    private sealed class FakesModule(Fakes fakes) : IModule
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton<IIdempotencyStore, OpenStore>();
            services.AddSingleton<ICommandHandler<RegisterOwner, RegisteredOwner>>(new Register(fakes));
            services.AddSingleton<ICommandHandler<VerifyOwnerEmail, OpenedSession>>(new Verify(fakes));
            services.AddSingleton<ICommandHandler<ResendOwnerVerification, Unit>>(new Resend(fakes));

            // The session routes of the same module are mapped too; nothing here calls them.
            services.AddSingleton<ICommandHandler<LogOut, Unit>>(new Unused<LogOut, Unit>());
            services.AddSingleton<IQueryHandler<GetCurrentSession, CurrentSessionView>>(new UnusedQuery());
        }
    }

    /// <summary>The registration use case, standing in for the real one.</summary>
    private sealed class Register(Fakes fakes) : ICommandHandler<RegisterOwner, RegisteredOwner>
    {
        public Task<Result<RegisteredOwner>> HandleAsync(RegisterOwner command, CancellationToken cancellationToken)
        {
            fakes.Registrations.Add(command);
            return Task.FromResult<Result<RegisteredOwner>>(fakes.Registered is null ? new RegisteredOwner(OwnerId) : fakes.Registered);
        }
    }

    /// <summary>The verification use case, standing in for the real one.</summary>
    private sealed class Verify(Fakes fakes) : ICommandHandler<VerifyOwnerEmail, OpenedSession>
    {
        public Task<Result<OpenedSession>> HandleAsync(VerifyOwnerEmail command, CancellationToken cancellationToken)
        {
            fakes.Verifications.Add(command);
            var start = Instant.FromUtc(2026, 11, 24, 12, 0);
            return Task.FromResult<Result<OpenedSession>>(fakes.Verified is null
                ? new OpenedSession(fakes.SessionId, new IssuedToken(fakes.Token, new byte[32]), start, start + Duration.FromDays(30))
                : fakes.Verified);
        }
    }

    /// <summary>The resend use case, standing in for the real one.</summary>
    private sealed class Resend(Fakes fakes) : ICommandHandler<ResendOwnerVerification, Unit>
    {
        public Task<Result<Unit>> HandleAsync(ResendOwnerVerification command, CancellationToken cancellationToken)
        {
            fakes.Resends.Add(command);
            return Task.FromResult<Result<Unit>>(Unit.Value);
        }
    }

    /// <summary>A use case of a route this suite never calls, registered so the route can be mapped.</summary>
    private sealed class Unused<TCommand, TResult> : ICommandHandler<TCommand, TResult>
        where TCommand : ICommand<TResult>
    {
        public Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No test calls this route.");
    }

    /// <summary>A query of a route this suite never calls, registered so the route can be mapped.</summary>
    private sealed class UnusedQuery : IQueryHandler<GetCurrentSession, CurrentSessionView>
    {
        public Task<Result<CurrentSessionView>> HandleAsync(GetCurrentSession query, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No test calls this route.");
    }

    /// <summary>An idempotency store that claims every key and replays nothing.</summary>
    private sealed class OpenStore : IIdempotencyStore
    {
        public ValueTask<IdempotencyClaim> ClaimAsync(string key, string fingerprint, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new IdempotencyClaim(IdempotencyOutcome.Claimed));

        public ValueTask CompleteAsync(string key, string fingerprint, StoredResponse response, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask ReleaseAsync(string key, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
