using System.Net;
using System.Text.Json;
using Frappe.Identity.Application.Credentials.ChangePassword;
using Frappe.Identity.Application.Owners;
using Frappe.Identity.Application.Owners.GetMe;
using Frappe.Identity.Application.Owners.LogInOwner;
using Frappe.Identity.Application.Owners.RegisterOwner;
using Frappe.Identity.Application.Owners.RequestPasswordReset;
using Frappe.Identity.Application.Owners.ResendOwnerVerification;
using Frappe.Identity.Application.Owners.ResetPassword;
using Frappe.Identity.Application.Owners.UpdateMe;
using Frappe.Identity.Application.Owners.VerifyOwnerEmail;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Application.Sessions.GetCurrentSession;
using Frappe.Identity.Application.Sessions.LogOut;
using Frappe.Identity.Application.Sessions.StepUp;
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
/// The log-in route (S7) over the real Api with the use case faked: what it hands the use case, 201 with the session in the body and the cookie, the
/// 403 of a pending owner with the owner id, the one 401, the throttles with <c>Retry-After</c> (the address's and the client's own), and
/// the contract.
/// </summary>
public sealed class IdentityLogInRouteTests
{
    private const string Path = "/v1/identity/sessions";

    private static readonly Guid OwnerId = Guid.Parse("0198f2a0-0000-7000-8000-0000000000aa");

    private static readonly object Credentials = new { email = "ana@example.com", password = "correct horse battery" };

    private static Task<WebApplication> StartAsync(Fakes fakes, Dictionary<string, string?>? settings = null) =>
        ApiHost.StartAsync([new FakesModule(fakes)], [new IdentityHttp()], settings);

    private static async Task<(string Code, JsonElement Error)> ProblemAsync(HttpResponseMessage response)
    {
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var error = problem.RootElement.GetProperty("errors")[0];
        return (error.GetProperty("code").GetString()!, error.Clone());
    }

    [Test]
    public async Task A_log_in_answers_201_with_the_session_in_the_body_and_the_cookie_and_hands_the_use_case_what_the_client_sent()
    {
        // Arrange
        var fakes = new Fakes();
        await using var app = await StartAsync(fakes);
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = JsonContent.Create(Credentials) };
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 test");

        // Act
        using var response = await client.SendAsync(request);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        var command = fakes.LogIns.Single();
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
            await Assert.That(response.Headers.Location).IsNull();
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(body.RootElement.GetProperty("token").GetString()).IsEqualTo(fakes.Token);
            await Assert.That(body.RootElement.GetProperty("id").GetGuid()).IsEqualTo(fakes.SessionId);
            await Assert.That(cookie).StartsWith($"{IdentitySessionCookie.Name}={fakes.Token}").And.Contains("httponly").And.Contains("secure");
            await Assert.That(command.Email).IsEqualTo("ana@example.com");
            await Assert.That(command.Password).IsEqualTo("correct horse battery");
            await Assert.That(command.UserAgent).IsEqualTo("Mozilla/5.0 test");
        }
    }

    [Test]
    public async Task A_pending_owner_is_answered_403_with_the_owner_id_to_verify_with_and_no_session()
    {
        // Arrange
        var fakes = new Fakes { Outcome = LogInOutcome.VerificationPending(OwnerId) };
        await using var app = await StartAsync(fakes);
        using var client = app.GetTestClient();

        // Act
        using var response = await client.PostAsJsonAsync(Path, Credentials);
        var (code, error) = await ProblemAsync(response);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            await Assert.That(code).IsEqualTo("identity.owner.verification_pending");
            await Assert.That(error.GetProperty("parameters").GetProperty("ownerId").GetString()).IsEqualTo(OwnerId.ToString());
            await Assert.That(error.GetProperty("message").GetString()).IsNotEmpty();
            await Assert.That(response.Headers.Contains("Set-Cookie")).IsFalse();
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        }
    }

    [Test]
    public async Task Wrong_credentials_are_the_one_401_with_its_text_in_the_requested_language()
    {
        // Arrange
        var fakes = new Fakes { Refusal = new UnauthenticatedError("identity.credentials.invalid") };
        await using var app = await StartAsync(fakes);
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = JsonContent.Create(Credentials) };
        request.Headers.TryAddWithoutValidation("Accept-Language", "es");

        // Act
        using var response = await client.SendAsync(request);
        var (code, error) = await ProblemAsync(response);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            await Assert.That(code).IsEqualTo("identity.credentials.invalid");
            await Assert.That(error.GetProperty("message").GetString()).Contains("contraseña");
            await Assert.That(response.Headers.Contains("Set-Cookie")).IsFalse();
        }
    }

    [Test]
    public async Task An_address_that_tries_too_often_is_answered_429_with_the_wait()
    {
        // Arrange
        var fakes = new Fakes { Refusal = new RateLimitedError("http.rate_limited", TimeSpan.FromMinutes(15)) };
        await using var app = await StartAsync(fakes);
        using var client = app.GetTestClient();

        // Act
        using var response = await client.PostAsJsonAsync(Path, Credentials);
        var (code, error) = await ProblemAsync(response);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
            await Assert.That(code).IsEqualTo("http.rate_limited");
            await Assert.That(response.Headers.RetryAfter?.Delta).IsEqualTo(TimeSpan.FromMinutes(15));
            await Assert.That(error.GetProperty("parameters").GetProperty("retryAfter").GetInt32()).IsEqualTo(900);
        }
    }

    [Test]
    public async Task Attempts_that_cannot_be_counted_are_answered_503_with_a_wait()
    {
        // Arrange
        var fakes = new Fakes { Refusal = new UnavailableError("identity.attempts.unavailable") };
        await using var app = await StartAsync(fakes);
        using var client = app.GetTestClient();

        // Act
        using var response = await client.PostAsJsonAsync(Path, Credentials);
        var (code, _) = await ProblemAsync(response);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
            await Assert.That(code).IsEqualTo("identity.attempts.unavailable");
            await Assert.That(response.Headers.RetryAfter).IsNotNull();
        }
    }

    [Test]
    public async Task A_client_address_that_logs_in_too_often_is_throttled_by_its_own_policy_before_the_use_case_runs()
    {
        // Arrange
        var fakes = new Fakes();
        await using var app = await StartAsync(fakes, new() { ["Identity:LogIn:RateLimit:PermitLimit"] = "2" });
        using var client = app.GetTestClient();

        // Act
        using var first = await client.PostAsJsonAsync(Path, Credentials);
        using var second = await client.PostAsJsonAsync(Path, Credentials);
        using var third = await client.PostAsJsonAsync(Path, Credentials);
        var (code, _) = await ProblemAsync(third);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.Created);
            await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.Created);
            await Assert.That(third.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
            await Assert.That(code).IsEqualTo("http.rate_limited");
            await Assert.That(third.Headers.RetryAfter).IsNotNull();
            await Assert.That(fakes.LogIns.Count).IsEqualTo(2);
        }
    }

    [Test]
    public async Task A_log_in_without_a_body_reaches_the_use_case_to_be_refused_there()
    {
        // Arrange
        var fakes = new Fakes { Refusal = new UnauthenticatedError("identity.credentials.invalid") };
        await using var app = await StartAsync(fakes);
        using var client = app.GetTestClient();

        // Act
        using var response = await client.PostAsJsonAsync(Path, new { });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            await Assert.That(fakes.LogIns.Single().Email).IsNull();
            await Assert.That(fakes.LogIns.Single().Password).IsNull();
        }
    }

    [Test]
    public async Task The_contract_lists_the_route_with_its_refusals_and_without_a_location_header()
    {
        // Arrange
        await using var app = await StartAsync(new Fakes());
        using var client = app.GetTestClient();

        // Act
        using var contract = await client.GetAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(await contract.Content.ReadAsStringAsync());

        // Assert
        var responses = document.RootElement.GetProperty("paths").GetProperty("/v1/identity/sessions").GetProperty("post").GetProperty("responses");
        var created = responses.GetProperty("201");
        using (Assert.Multiple())
        {
            await Assert.That(created.TryGetProperty("headers", out var headers) && headers.TryGetProperty("Location", out _)).IsFalse();
            await Assert.That(responses.EnumerateObject().Select(r => r.Name)).Contains("401").And.Contains("403").And.Contains("429")
                .And.Contains("503");
        }
    }

    /// <summary>What the faked use case saw and the answers it gives.</summary>
    private sealed class Fakes
    {
        public List<LogInOwner> LogIns { get; } = [];

        public Error? Refusal { get; init; }

        public LogInOutcome? Outcome { get; init; }

        public Guid SessionId { get; } = Guid.CreateVersion7();

        public string Token { get; } = new Frappe.Identity.Infrastructure.Sessions.SessionTokens().Issue().Value;
    }

    /// <summary>Registers the fake use case; the other routes of the module are mapped too, so their use cases are registered unused.</summary>
    private sealed class FakesModule(Fakes fakes) : IModule
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton<ICommandHandler<LogInOwner, LogInOutcome>>(new LogIn(fakes));
            services.AddSingleton<ICommandHandler<RegisterOwner, RegisteredOwner>>(new Unused<RegisterOwner, RegisteredOwner>());
            services.AddSingleton<ICommandHandler<VerifyOwnerEmail, OpenedSession>>(new Unused<VerifyOwnerEmail, OpenedSession>());
            services.AddSingleton<ICommandHandler<ResendOwnerVerification, Unit>>(new Unused<ResendOwnerVerification, Unit>());
            services.AddSingleton<IQueryHandler<GetMe, OwnerProfile>>(new UnusedQuery<GetMe, OwnerProfile>());
            services.AddSingleton<ICommandHandler<UpdateMe, Unit>>(new Unused<UpdateMe, Unit>());
            services.AddSingleton<ICommandHandler<LogOut, Unit>>(new Unused<LogOut, Unit>());
            services.AddSingleton<ICommandHandler<ChangePassword, Unit>>(new Unused<ChangePassword, Unit>());
            services.AddSingleton<ICommandHandler<StepUp, SteppedUp>>(new Unused<StepUp, SteppedUp>());
            services.AddSingleton<ICommandHandler<RequestPasswordReset, PasswordResetRequested>>(
                new Unused<RequestPasswordReset, PasswordResetRequested>());
            services.AddSingleton<ICommandHandler<ResetPassword, Unit>>(new Unused<ResetPassword, Unit>());
            services.AddSingleton<IQueryHandler<GetCurrentSession, CurrentSessionView>>(new UnusedQuery());
        }
    }

    /// <summary>The log-in use case, standing in for the real one.</summary>
    private sealed class LogIn(Fakes fakes) : ICommandHandler<LogInOwner, LogInOutcome>
    {
        public Task<Result<LogInOutcome>> HandleAsync(LogInOwner command, CancellationToken cancellationToken)
        {
            fakes.LogIns.Add(command);
            var start = Instant.FromUtc(2026, 11, 24, 12, 0);
            var session = new OpenedSession(fakes.SessionId, new IssuedToken(fakes.Token, new byte[32]), start, start + Duration.FromDays(30));
            var opened = LogInOutcome.Opened(session);
            return Task.FromResult<Result<LogInOutcome>>(fakes.Refusal is not null ? fakes.Refusal : fakes.Outcome ?? opened);
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

    /// <summary>A query of a route this suite never calls, registered so the route can be mapped.</summary>
    private sealed class UnusedQuery<TQuery, TResult> : IQueryHandler<TQuery, TResult>
        where TQuery : IQuery<TResult>
    {
        public Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No test calls this route.");
    }
}
