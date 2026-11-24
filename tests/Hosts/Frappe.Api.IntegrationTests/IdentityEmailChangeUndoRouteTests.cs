using System.Net;
using System.Text.Json;
using Frappe.Identity.Application.Owners.UndoEmailChange;
using Frappe.Modules;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Api.IntegrationTests;

/// <summary>
/// The undo route of an email change (S15) over the real host with the use case faked: anonymous, it hands the use case the token, answers 204
/// without <c>Location</c> or cache, holds every answer to the floor, and the refusal is the 422 <c>identity.undo.invalid</c> with its text.
/// </summary>
public sealed class IdentityEmailChangeUndoRouteTests
{
    private const string Path = "/v1/identity/email-change-undos";

    private static HttpRequestMessage Post(object body) => new(HttpMethod.Post, Path) { Content = JsonContent.Create(body) };

    [Test]
    public async Task A_token_is_handed_to_the_use_case_and_answers_204_without_cache_or_location_after_the_floor()
    {
        // Arrange
        var fakes = new Fakes();
        await using var host = await IdentitySessionHost.StartAsync(extra: new FakesModule(fakes));
        using var client = host.App.GetTestClient();
        host.Time.FrozenTimestamp = 0;

        // Act
        using var response = await client.SendAsync(Post(new { token = "id.secret" }));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(response.Headers.Location).IsNull();
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(fakes.Undos.Single().Token).IsEqualTo("id.secret");
            await Assert.That(host.Time.Waits).Contains(EmailChangeUndoLimits.Floor);
        }
    }

    [Test]
    public async Task A_refused_token_is_the_422_with_its_code_and_text_held_to_the_same_floor()
    {
        // Arrange
        var fakes = new Fakes { Answer = Error.RuleViolation("identity.undo.invalid") };
        await using var host = await IdentitySessionHost.StartAsync(extra: new FakesModule(fakes));
        using var client = host.App.GetTestClient();
        host.Time.FrozenTimestamp = 0;

        // Act
        using var response = await client.SendAsync(Post(new { token = "junk" }));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var error = document.RootElement.GetProperty("errors")[0];

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.UnprocessableEntity);
            await Assert.That(error.GetProperty("code").GetString()).IsEqualTo("identity.undo.invalid");
            await Assert.That(error.GetProperty("message").GetString()).IsNotEmpty();
            await Assert.That(host.Time.Waits).Contains(EmailChangeUndoLimits.Floor);
        }
    }

    /// <summary>What the faked use case saw and the answer it gives.</summary>
    private sealed class Fakes
    {
        public List<UndoEmailChange> Undos { get; } = [];

        public Error? Answer { get; init; }
    }

    /// <summary>Registers the faked use case.</summary>
    private sealed class FakesModule(Fakes fakes) : IModule
    {
        public void AddServices(IServiceCollection services) =>
            services.AddFakeCommand<UndoEmailChange, Unit>(new UndoHandler(fakes));
    }

    /// <summary>The undo use case, standing in for the real one.</summary>
    private sealed class UndoHandler(Fakes fakes) : ICommandHandler<UndoEmailChange, Unit>
    {
        public Task<Result<Unit>> HandleAsync(UndoEmailChange command, CancellationToken cancellationToken)
        {
            fakes.Undos.Add(command);
            return Task.FromResult<Result<Unit>>(fakes.Answer is null ? Unit.Value : fakes.Answer);
        }
    }
}
