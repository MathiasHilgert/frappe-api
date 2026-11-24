using System.Net;
using System.Text.Json;
using Frappe.Access.Contracts;
using Frappe.Identity.Application.Owners.ConfirmEmailChange;
using Frappe.Identity.Application.Owners.RequestEmailChange;
using Frappe.Modules;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Api.IntegrationTests;

/// <summary>
/// The email change routes (S15) over the real session authentication: the request needs a stepped-up session and answers 202 without
/// <c>Location</c> or cache, the confirmation hands the use case the caller, the session and the id of the URL, and answers 204.
/// </summary>
public sealed class IdentityEmailChangeRouteTests
{
    private const string Path = "/v1/identity/me/email-changes";

    private static HttpRequestMessage Post(string path, string? token, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (token is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        }

        return request;
    }

    [Test]
    public async Task Without_a_step_up_the_request_is_403_and_never_reaches_the_use_case()
    {
        // Arrange
        var fakes = new Fakes();
        await using var host = await IdentitySessionHost.StartAsync(extra: new FakesModule(fakes));
        var (principal, session, token) = host.Seed();
        using var client = host.App.GetTestClient();

        // Act
        using var response = await client.SendAsync(Post(Path, token, new { email = "new@example.com" }));
        using var anonymous = await client.SendAsync(Post(Path, null, new { email = "new@example.com" }));

        // Assert
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            await Assert.That(document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo("access.step_up.required");
            await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            await Assert.That(fakes.Requests).IsEmpty();
        }
    }

    [Test]
    public async Task With_a_step_up_the_request_hands_the_use_case_the_caller_and_answers_202_with_the_id_and_no_location_or_cache()
    {
        // Arrange
        var fakes = new Fakes();
        await using var host = await IdentitySessionHost.StartAsync(extra: new FakesModule(fakes));
        var (principal, session, token) = host.Seed();
        session.StepUp(IdentitySessionHost.Start);
        using var client = host.App.GetTestClient();

        // Act
        using var response = await client.SendAsync(Post(Path, token, new { email = "new@example.com" }));

        // Assert
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var command = fakes.Requests.Single();
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
            await Assert.That(response.Headers.Location).IsNull();
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(document.RootElement.GetProperty("id").GetGuid()).IsEqualTo(Fakes.ChangeId);
            await Assert.That(fakes.Callers.Single()).IsEqualTo((principal.Id, session.Id));
            await Assert.That(command.NewEmail).IsEqualTo("new@example.com");
            await Assert.That(command.ToString()).DoesNotContain("new@example.com");
        }
    }

    [Test]
    public async Task The_confirmation_hands_the_use_case_the_caller_the_session_and_the_id_and_answers_204_without_a_step_up()
    {
        // Arrange
        var fakes = new Fakes();
        await using var host = await IdentitySessionHost.StartAsync(extra: new FakesModule(fakes));
        var (principal, session, token) = host.Seed();
        using var client = host.App.GetTestClient();

        // Act
        using var response = await client.SendAsync(Post($"{Path}/{Fakes.ChangeId}/confirmation", token, new { code = "123456" }));

        // Assert
        var command = fakes.Confirmations.Single();
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(fakes.Callers.Single()).IsEqualTo((principal.Id, session.Id));
            await Assert.That(command.ChangeId).IsEqualTo(Fakes.ChangeId.ToString());
            await Assert.That(command.Code).IsEqualTo("123456");
            await Assert.That(command.ToString()).DoesNotContain("123456");
        }
    }

    /// <summary>What the faked use cases saw.</summary>
    private sealed class Fakes
    {
        /// <summary>The principal and session the use cases saw through the caller.</summary>
        public List<(Guid Principal, Guid Session)> Callers { get; } = [];

        public void Seen(ICaller caller) => Callers.Add((caller.RequiredPrincipalId, caller.RequiredSessionId));

        public static readonly Guid ChangeId = Guid.Parse("0192b5f0-0000-7000-8000-000000000001");

        public List<RequestEmailChange> Requests { get; } = [];

        public List<ConfirmEmailChange> Confirmations { get; } = [];
    }

    /// <summary>Registers the faked use cases.</summary>
    private sealed class FakesModule(Fakes fakes) : IModule
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddFakeCommand<RequestEmailChange, EmailChangeRequested>(new RequestHandler(fakes), fakes.Seen);
            services.AddFakeCommand<ConfirmEmailChange, Unit>(new ConfirmHandler(fakes), fakes.Seen);
        }
    }

    /// <summary>The request use case, standing in for the real one.</summary>
    private sealed class RequestHandler(Fakes fakes) : ICommandHandler<RequestEmailChange, EmailChangeRequested>
    {
        public Task<Result<EmailChangeRequested>> HandleAsync(RequestEmailChange command, CancellationToken cancellationToken)
        {
            fakes.Requests.Add(command);
            return Task.FromResult<Result<EmailChangeRequested>>(new EmailChangeRequested(Fakes.ChangeId));
        }
    }

    /// <summary>The confirmation use case, standing in for the real one.</summary>
    private sealed class ConfirmHandler(Fakes fakes) : ICommandHandler<ConfirmEmailChange, Unit>
    {
        public Task<Result<Unit>> HandleAsync(ConfirmEmailChange command, CancellationToken cancellationToken)
        {
            fakes.Confirmations.Add(command);
            return Task.FromResult<Result<Unit>>(Unit.Value);
        }
    }
}
