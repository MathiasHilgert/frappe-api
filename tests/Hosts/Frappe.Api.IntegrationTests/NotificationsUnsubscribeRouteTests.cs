using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Frappe.Http;
using Frappe.Modules;
using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Application.Requests.Unsubscribe;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Http;
using Frappe.Notifications.Infrastructure.Unsubscribe;
using Frappe.Results;
using Frappe.Testing;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Frappe.Api.IntegrationTests;

/// <summary>
/// The one-click unsubscribe route (S5, S9, S16): anonymous but signed by its token, 204 for a valid token however the body comes, 400 with its
/// code for any other, no redirect, no antiforgery, a limit per client and one per token that fails open, its operation in the contract, and
/// the token in no log and no trace (SEC-09).
/// </summary>
public sealed class NotificationsUnsubscribeRouteTests
{
    private const string Route = "/v1/notifications/unsubscriptions";

    private static readonly Dictionary<string, string?> Settings = new()
    {
        ["Notifications:Unsubscribe:SigningKey"] = Convert.ToBase64String(Enumerable.Repeat((byte)5, 32).ToArray()),
    };

    private static async Task<WebApplication> StartAsync(
        FakeHandler handler, FakeStore? store = null, Dictionary<string, string?>? extra = null, CapturedLogs? logs = null)
    {
        var settings = new Dictionary<string, string?>(Settings);
        foreach (var (key, value) in extra ?? [])
        {
            settings[key] = value;
        }

        return await ApiHost.StartAsync([new FakeModule(handler, store)], [new NotificationsHttp()], settings, logs: logs);
    }

    private static string TokenOf(WebApplication app, string recipient = "ref:account:42") =>
        app.Services.GetRequiredService<IUnsubscribeTokens>().Issue(RecipientKey.Parse(recipient).Value, NotificationCategory.Marketing);

    [Test]
    public async Task A_valid_token_is_a_204_that_never_redirects_as_the_mail_client_posts_it()
    {
        // Arrange
        var handler = new FakeHandler();
        await using var app = await StartAsync(handler);
        using var client = app.GetTestClient();
        var url = $"{Route}?token={TokenOf(app)}";
        using var multipart = new MultipartFormDataContent { { new StringContent("One-Click"), "List-Unsubscribe" } };

        // Act
        using var urlencoded = await client.PostAsync(url, new FormUrlEncodedContent([new("List-Unsubscribe", "One-Click")]));
        using var multipartResponse = await client.PostAsync(url, multipart);
        using var noBody = await client.PostAsync(url, null);

        // Assert
        using (Assert.Multiple())
        {
            foreach (var response in new[] { urlencoded, multipartResponse, noBody })
            {
                await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
                await Assert.That(response.Headers.Location).IsNull();
                await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            }

            await Assert.That(handler.Received.Count).IsEqualTo(3);
            await Assert.That(handler.Received.Select(c => c.Token).Distinct().Count()).IsEqualTo(1);
        }
    }

    [Test]
    public async Task A_body_of_any_other_type_is_a_415_that_reaches_nothing()
    {
        // Arrange
        var handler = new FakeHandler();
        await using var app = await StartAsync(handler);
        using var client = app.GetTestClient();

        // Act
        using var response = await client.PostAsync($"{Route}?token={TokenOf(app)}", new StringContent("{}", Encoding.UTF8, "application/json"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.UnsupportedMediaType);
            await Assert.That(handler.Received).IsEmpty();
        }
    }

    [Test]
    public async Task A_missing_tampered_or_foreign_token_is_a_400_with_the_code_and_in_the_callers_language()
    {
        // Arrange
        var handler = new FakeHandler();
        await using var app = await StartAsync(handler);
        using var client = app.GetTestClient();
        var tampered = TokenOf(app)[..^3] + "AAA";
        using var english = new HttpRequestMessage(HttpMethod.Post, $"{Route}?token=garbage");
        english.Headers.AcceptLanguage.ParseAdd("en");

        // Act
        using var missing = await client.PostAsync(Route, null);
        using var empty = await client.PostAsync($"{Route}?token=", null);
        using var forged = await client.PostAsync($"{Route}?token={tampered}", null);
        using var localized = await client.SendAsync(english);
        using var problem = JsonDocument.Parse(await localized.Content.ReadAsStringAsync());

        // Assert
        var error = problem.RootElement.GetProperty("errors")[0];
        using (Assert.Multiple())
        {
            foreach (var response in new[] { missing, empty, forged, localized })
            {
                await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
                await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
            }

            await Assert.That(error.GetProperty("code").GetString()).IsEqualTo("notifications.unsubscribe_token.invalid");
            await Assert.That(error.GetProperty("message").GetString()).Contains("unsubscribe");
            await Assert.That(error.GetProperty("message").GetString()).Contains("original email");
            await Assert.That(handler.Verified).IsEmpty();
        }
    }

    [Test]
    [Arguments("es", "correo original")]
    [Arguments("es-AR", "correo original")]
    [Arguments("pt-BR", "e-mail original")]
    public async Task The_invalid_token_message_gives_the_next_step_in_each_language(string language, string nextStep)
    {
        // Arrange
        await using var app = await StartAsync(new FakeHandler());
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Route}?token=garbage");
        request.Headers.AcceptLanguage.ParseAdd(language);

        // Act
        using var response = await client.SendAsync(request);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        await Assert.That(problem.RootElement.GetProperty("errors")[0].GetProperty("message").GetString()).Contains(nextStep);
    }

    [Test]
    public async Task A_conflict_or_an_outage_is_answered_as_such_so_the_caller_may_try_again()
    {
        // Arrange
        var handler = new FakeHandler { Answer = new ConflictError("commit.conflict") };
        await using var app = await StartAsync(handler);
        using var client = app.GetTestClient();

        // Act
        using var response = await client.PostAsync($"{Route}?token={TokenOf(app)}", null);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task One_token_may_be_used_only_so_often_and_another_token_has_its_own_count()
    {
        // Arrange
        var store = new FakeStore();
        await using var app = await StartAsync(new FakeHandler(), store);
        using var client = app.GetTestClient();
        var token = TokenOf(app);
        var url = $"{Route}?token={token}";

        // Act
        var statuses = new List<HttpStatusCode>();
        for (var call = 0; call < 11; call++)
        {
            using var response = await client.PostAsync(url, null);
            statuses.Add(response.StatusCode);
        }

        using var limited = await client.PostAsync(url, null);
        using var other = await client.PostAsync($"{Route}?token={TokenOf(app, "ref:account:43")}", null);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(statuses.Take(10)).IsEquivalentTo(Enumerable.Repeat(HttpStatusCode.NoContent, 10));
            await Assert.That(statuses[10]).IsEqualTo(HttpStatusCode.TooManyRequests);
            await Assert.That(limited.Headers.RetryAfter?.Delta).IsNotNull();
            await Assert.That(other.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(store.Keys.Where(key => key.StartsWith("notifications:unsubscribe:", StringComparison.Ordinal)).Distinct().Count())
                .IsEqualTo(2);
            await Assert.That(store.Keys.Any(key => key.Contains(token[10..], StringComparison.Ordinal))).IsFalse();
        }
    }

    [Test]
    public async Task A_token_that_fails_verification_never_reaches_the_limiter_and_a_reformatted_one_does_not_get_a_count_of_its_own()
    {
        // Arrange
        var store = new FakeStore();
        await using var app = await StartAsync(new FakeHandler(), store);
        using var client = app.GetTestClient();
        var token = TokenOf(app);

        // Act
        var statuses = new List<HttpStatusCode>();
        foreach (var bad in new[] { "garbage", token[..^3] + "AAA", "0" + token, token + "=" })
        {
            using var response = await client.PostAsync($"{Route}?token={bad}", null);
            statuses.Add(response.StatusCode);
        }

        using var missing = await client.PostAsync(Route, null);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(statuses).IsEquivalentTo(Enumerable.Repeat(HttpStatusCode.BadRequest, 4));
            await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(store.Keys.Where(key => key.StartsWith("notifications:unsubscribe:", StringComparison.Ordinal))).IsEmpty();
        }
    }

    [Test]
    public async Task The_limit_per_token_fails_open_when_the_store_does_not_answer_and_leaves_it_alone_for_a_while()
    {
        // Arrange
        var handler = new FakeHandler();
        var store = new FakeStore { Failing = true };
        await using var app = await StartAsync(handler, store);
        using var client = app.GetTestClient();

        // Act
        var statuses = new List<HttpStatusCode>();
        for (var call = 0; call < 12; call++)
        {
            using var response = await client.PostAsync($"{Route}?token={TokenOf(app)}", null);
            statuses.Add(response.StatusCode);
        }

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(statuses.Distinct()).IsEquivalentTo([HttpStatusCode.NoContent]);
            await Assert.That(handler.Received.Count).IsEqualTo(12);
            await Assert.That(store.Calls).IsLessThanOrEqualTo(3);
        }
    }

    [Test]
    public async Task The_route_has_a_rate_limit_per_client_of_its_own_that_the_section_sets()
    {
        // Arrange
        await using var app = await StartAsync(new FakeHandler(), extra: new() { ["Notifications:Unsubscribe:RateLimit:PermitLimit"] = "2" });
        using var client = app.GetTestClient();

        // Act
        var statuses = new List<HttpStatusCode>();
        for (var call = 0; call < 3; call++)
        {
            using var response = await client.PostAsync($"{Route}?token={TokenOf(app, $"ref:account:{call}")}", null);
            statuses.Add(response.StatusCode);
        }

        // Assert
        await Assert.That(statuses).IsEquivalentTo([HttpStatusCode.NoContent, HttpStatusCode.NoContent, HttpStatusCode.TooManyRequests]);
    }

    [Test]
    public async Task The_token_appears_in_no_log_and_no_trace_on_a_204_a_400_or_a_429()
    {
        // Arrange
        using var logs = new CapturedLogs();
        var queries = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                foreach (var tag in activity.TagObjects)
                {
                    queries.Enqueue($"{tag.Key}={tag.Value}");
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        var limit = new Dictionary<string, string?> { ["Notifications:Unsubscribe:RateLimit:PermitLimit"] = "2" };
        await using var app = await StartAsync(new FakeHandler(), extra: limit, logs: logs);
        using var client = app.GetTestClient();
        var token = TokenOf(app);

        // Act
        using var ok = await client.PostAsync($"{Route}?token={token}&x=1", null);
        using var bad = await client.PostAsync($"{Route}?token={token}0", null);
        using var limited = await client.PostAsync($"{Route}?token={token}", null);

        // Assert
        var everything = string.Join('\n', logs.Entries.Select(entry => entry.Message + entry.Exception)) + "\n" + string.Join('\n', queries);
        using (Assert.Multiple())
        {
            await Assert.That(ok.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(bad.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(limited.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
            await Assert.That(everything).DoesNotContain(token[10..]);
            await Assert.That(queries.Where(q => q.StartsWith("url.query=", StringComparison.Ordinal)))
                .Contains("url.query=token=REDACTED&x=1");
        }
    }

    [Test]
    public async Task The_contract_documents_the_operation_its_query_token_its_two_body_types_and_its_answers()
    {
        // Arrange
        await using var app = await StartAsync(new FakeHandler());
        using var client = app.GetTestClient();

        // Act
        using var contract = await client.GetAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(await contract.Content.ReadAsStringAsync());

        // Assert
        var post = document.RootElement.GetProperty("paths").GetProperty(Route).GetProperty("post");
        var responses = post.GetProperty("responses");
        var token = post.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "token");
        var content = post.GetProperty("requestBody").GetProperty("content").EnumerateObject().Select(p => p.Name);
        using (Assert.Multiple())
        {
            await Assert.That(post.GetProperty("operationId").GetString()).IsEqualTo("Unsubscribe");
            await Assert.That(token.GetProperty("in").GetString()).IsEqualTo("query");
            await Assert.That(content).Contains("application/x-www-form-urlencoded").And.Contains("multipart/form-data");
            await Assert.That(responses.TryGetProperty("204", out _)).IsTrue();
            await Assert.That(responses.TryGetProperty("400", out _)).IsTrue();
            await Assert.That(responses.TryGetProperty("429", out _)).IsTrue();
        }
    }

    [Test]
    public async Task RequestTelemetry_hides_only_the_sensitive_values_of_a_query()
    {
        // Arrange
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.QueryString = new("?a=1&Token=secret&token=other&b=x%20y&c");
        using var activity = new Activity("test");

        // Act
        RequestTelemetry.RedactQuery(activity, context.Request);

        // Assert
        await Assert.That(activity.GetTagItem("url.query")).IsEqualTo("a=1&token=REDACTED&token=REDACTED&b=x%20y&c=");
    }

    /// <summary>The use case, standing in for the real one (which needs a database): it verifies the token alike and keeps what it saw.</summary>
    private sealed class FakeHandler : ICommandHandler<Unsubscribe, Unit>
    {
        public List<Unsubscribe> Received { get; } = [];

        public List<RecipientKey> Verified { get; } = [];

        public Error? Answer { get; init; }

        public IUnsubscribeTokens? Tokens { get; set; }

        public Task<Result<Unit>> HandleAsync(Unsubscribe command, CancellationToken cancellationToken)
        {
            Received.Add(command);
            var claim = Tokens!.Verify(command.Token);
            if (!claim.IsSuccess)
            {
                return Task.FromResult(Result.Failure<Unit>(claim.Errors));
            }

            Verified.Add(claim.Value.Recipient);
            return Task.FromResult<Result<Unit>>(Answer is null ? Unit.Value : Answer);
        }
    }

    /// <summary>The platform's shared window store in memory: counts by key and can be told to fail.</summary>
    private sealed class FakeStore : IRateLimitStore
    {
        private readonly Dictionary<string, long> _counts = [];

        public bool Failing { get; init; }

        public int Calls { get; private set; }

        public List<string> Keys { get; } = [];

        public ValueTask<RateLimitWindow> HitAsync(string key, int permits, TimeSpan window, CancellationToken cancellationToken)
        {
            Calls++;
            if (Failing)
            {
                throw new InvalidOperationException("The store is down.");
            }

            lock (_counts)
            {
                Keys.Add(key);
                _counts[key] = _counts.GetValueOrDefault(key) + permits;
                return ValueTask.FromResult(new RateLimitWindow(_counts[key], window));
            }
        }
    }

    /// <summary>Registers the fake use case, the real tokens and, when given, the fake window store.</summary>
    private sealed class FakeModule(FakeHandler handler, FakeStore? store) : IModule
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddOptions<UnsubscribeOptions>().BindConfiguration(UnsubscribeOptions.Section);
            services.AddSingleton<IUnsubscribeTokens, HmacUnsubscribeTokens>();
            services.AddSingleton<ICommandHandler<Unsubscribe, Unit>>(provider =>
            {
                handler.Tokens = provider.GetRequiredService<IUnsubscribeTokens>();
                return handler;
            });
            if (store is not null)
            {
                services.RemoveAll<IRateLimitStore>();
                services.AddSingleton<IRateLimitStore>(store);
            }
        }
    }
}
