using Frappe.Application.Abstractions.Realtime;
using Frappe.Platform.Health;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NATS.Client.JetStream;
using NATS.Net;

namespace Frappe.Platform.Realtime;

public static class RealtimeRegistration
{
    /// <summary>Who may read a business's stream: a signed-in member of it, asked through <see cref="IBusinessMembership"/>.</summary>
    public const string StreamPolicy = "realtime.stream";

    /// <summary>Local development broker, used when no "nats" connection string is configured.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1075:URIs should not be hardcoded",
        Justification = "Deliberate local-development fallback; deployments set ConnectionStrings:nats.")]
    private const string DefaultNatsUrl = "nats://localhost:4222";

    public static IServiceCollection AddRealtime(this IServiceCollection services, IConfiguration configuration)
    {
        var url = configuration.GetConnectionString("nats") ?? DefaultNatsUrl;

        // Factory registrations: the container owns, and disposes, what it creates.
        services.AddSingleton(_ => new NatsClient(url));
        services.AddSingleton<INatsJSContext>(sp => sp.GetRequiredService<NatsClient>().CreateJetStreamContext());
        services.AddSingleton<IRealtimePublisher, NatsRealtimePublisher>();
        services.AddSingleton<RealtimeSseReader>();
        services.AddHostedService<RealtimeStreamProvisioner>();

        services.TryAddSingleton<IBusinessMembership, DenyAllBusinessMembership>();
        services.AddSingleton<IAuthorizationHandler, BusinessMemberHandler>();
        services.AddAuthorizationBuilder().AddPolicy(StreamPolicy, policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(new BusinessMemberRequirement()));

        // NATS only feeds live SSE and the outbox retries delivery, so its outage degrades, never blocks.
        services.AddHealthChecks().AddDependency("nats", DependencyCriticality.NonCritical, async (sp, ct) =>
            await sp.GetRequiredService<NatsClient>().PingAsync(ct));

        return services;
    }

    public static IEndpointRouteBuilder MapRealtime(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/stream", Stream)
            .RequireAuthorization(StreamPolicy)
            .WithName("Stream")
            .WithSummary("Server-sent events of one business the caller belongs to; 403 for any other business.");

        return endpoints;
    }

    private static ServerSentEventsResult<System.Text.Json.JsonElement> Stream(
        Guid businessId,
        [FromHeader(Name = "Last-Event-ID")] ulong? lastEventId,
        RealtimeSseReader reader,
        CancellationToken cancellationToken) =>
        TypedResults.ServerSentEvents(reader.ReadAsync(businessId, lastEventId, cancellationToken));
}
