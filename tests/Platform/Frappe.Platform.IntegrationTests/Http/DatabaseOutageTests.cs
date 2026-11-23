using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Frappe.Http;
using Frappe.Persistence;
using Frappe.Platform.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Frappe.Platform.IntegrationTests.Http;

/// <summary>
/// A database that does not answer is an outage the client may wait out (<c>503</c> with <c>Retry-After</c>), not a bug
/// (<c>500</c>); a database that answers with a refusal (a constraint) is still a failure of the request.
/// </summary>
public sealed class DatabaseOutageTests
{
    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddFrappeHttp(AnonymousRateLimit.Default).AddErrorMessages(PlatformTestMessages.Assembly);

        var app = builder.Build();
        app.UseFrappeHttp();
        var v1 = app.MapGroup("/v1").NegotiateRequestLanguage();
        v1.MapGet("/unreachable", async Task<string> () =>
        {
            // A real connection attempt to a port nobody listens on: the exception Npgsql raises when the server is down.
            await using var connection = new NpgsqlConnection($"Host=127.0.0.1;Port={ClosedPort()};Timeout=2;Pooling=false");
            await connection.OpenAsync();
            return "unreachable";
        });
        v1.MapGet("/ef-unreachable", async Task<string> () =>
        {
            // The same through EF Core, as a module's handler reaches its database.
            var options = new DbContextOptionsBuilder<DbContext>()
                .UseModulePostgres($"Host=127.0.0.1;Port={ClosedPort()};Timeout=2;Pooling=false", "outage")
                .Options;
            await using var db = new DbContext(options);
            var rows = await db.Database.SqlQueryRaw<int>("SELECT 1 AS \"Value\"").ToListAsync();
            return rows.Single().ToString(System.Globalization.CultureInfo.InvariantCulture);
        });
        v1.MapGet("/timeout", string () => throw new NpgsqlException(
            "Exception while reading from stream", new TimeoutException("Timeout during reading attempt")));
        v1.MapGet("/wrapped", string () => throw new DbUpdateException(
            "An error occurred while saving the entity changes.",
            new NpgsqlException("Failed to connect", new SocketException((int)SocketError.ConnectionRefused))));
        // A statement the server cut at the role's statement_timeout, raised directly and as EF Core wraps it on a save.
        v1.MapGet("/statement-timeout", string () => throw new PostgresException(
            "canceling statement due to statement timeout", "ERROR", "ERROR", "57014"));
        v1.MapGet("/statement-timeout-on-save", string () => throw new DbUpdateException(
            "An error occurred while saving the entity changes.",
            new PostgresException("canceling statement due to statement timeout", "ERROR", "ERROR", "57014")));
        v1.MapGet("/shutdown", string () => throw new PostgresException(
            "terminating connection due to administrator command", "FATAL", "FATAL", "57P01"));
        v1.MapGet("/constraint", string () => throw new DbUpdateException(
            "An error occurred while saving the entity changes.",
            new PostgresException("duplicate key value violates unique constraint", "ERROR", "ERROR", "23505")));
        v1.MapGet("/other-unavailable", string () => throw new BadHttpRequestException(
            "A dependency the framework reads is down.", StatusCodes.Status503ServiceUnavailable));
        v1.MapGet("/business", string () => throw new InvalidOperationException("not a database failure"));

        await app.StartAsync();
        return app;
    }

    /// <summary>A loopback port that was free a moment ago and has no listener now.</summary>
    private static int ClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<HttpResponseMessage> GetAsync(WebApplication app, string path, string acceptLanguage)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        return await app.GetTestClient().SendAsync(request);
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    [Test]
    [Arguments("/v1/unreachable")]
    [Arguments("/v1/ef-unreachable")]
    [Arguments("/v1/timeout")]
    [Arguments("/v1/wrapped")]
    [Arguments("/v1/shutdown")]
    [Arguments("/v1/statement-timeout")]
    [Arguments("/v1/statement-timeout-on-save")]
    public async Task A_database_that_does_not_answer_is_a_503_problem_with_Retry_After(string path)
    {
        // Arrange
        await using var app = await StartAsync();

        // Act
        using var response = await GetAsync(app, path, "en");
        var body = await response.Content.ReadAsStringAsync();
        var problem = await ProblemAsync(response);

        // Assert
        var error = problem.GetProperty("errors")[0];
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
            await Assert.That(response.Headers.RetryAfter?.Delta)
                .IsEqualTo(TimeSpan.FromSeconds(HttpRegistration.DatabaseUnavailableRetryAfterSeconds));
            await Assert.That(body).DoesNotContain("127.0.0.1");
            await Assert.That(body).DoesNotContain("Timeout during reading");
            await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:unavailable");
            await Assert.That(problem.GetProperty("status").GetInt32()).IsEqualTo(503);
            await Assert.That(problem.GetProperty("errors").GetArrayLength()).IsEqualTo(1);
            await Assert.That(error.GetProperty("code").GetString()).IsEqualTo(HttpRegistration.DatabaseUnavailableCode);
            await Assert.That(error.GetProperty("code").GetString()).IsEqualTo("platform.database.unavailable");
            await Assert.That(problem.GetProperty("title").GetString()).IsEqualTo("The service is not available right now.");
            await Assert.That(problem.GetProperty("detail").GetString())
                .IsEqualTo("The database is not available right now. Try again in 30 seconds.");
            await Assert.That(problem.GetProperty("traceId").GetString()).IsNotNull();
        }
    }

    [Test]
    [Arguments("es-AR", "La base de datos no está disponible en este momento. Vuelva a intentarlo en 30 segundos.", "es")]
    [Arguments("pt-BR", "O banco de dados não está disponível no momento. Tente novamente em 30 segundos.", "pt-BR")]
    public async Task The_outage_is_told_in_the_request_language(string acceptLanguage, string detail, string language)
    {
        // Arrange
        await using var app = await StartAsync();

        // Act
        using var response = await GetAsync(app, "/v1/timeout", acceptLanguage);
        var problem = await ProblemAsync(response);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
            await Assert.That(problem.GetProperty("detail").GetString()).IsEqualTo(detail);
            await Assert.That(response.Content.Headers.ContentLanguage.ToArray()).IsEquivalentTo([language]);
        }
    }

    [Test]
    [Arguments("/v1/constraint")]
    [Arguments("/v1/business")]
    public async Task A_database_that_answers_with_a_refusal_and_any_other_exception_stay_a_500(string path)
    {
        // Arrange
        await using var app = await StartAsync();

        // Act
        using var response = await GetAsync(app, path, "en");
        var problem = await ProblemAsync(response);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
            await Assert.That(response.Headers.RetryAfter).IsNull();
            await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:unexpected");
            await Assert.That(problem.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo(HttpRegistration.UnexpectedCode);
        }
    }

    [Test]
    public async Task Another_exception_answered_503_is_the_generic_unavailable_problem_not_a_database_outage()
    {
        // Arrange
        await using var app = await StartAsync();

        // Act
        using var response = await GetAsync(app, "/v1/other-unavailable", "en");
        var problem = await ProblemAsync(response);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
            await Assert.That(response.Headers.RetryAfter).IsNull();
            await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:unavailable");
            await Assert.That(problem.GetProperty("status").GetInt32()).IsEqualTo(503);
            await Assert.That(problem.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo(HttpRegistration.UnexpectedCode);
            await Assert.That(await response.Content.ReadAsStringAsync()).DoesNotContain(HttpRegistration.DatabaseUnavailableCode);
        }
    }

    [Test]
    [Arguments("40001", true)]
    [Arguments("40P01", true)]
    [Arguments("53300", true)]
    [Arguments("57P03", true)]
    [Arguments("57014", true)]
    [Arguments("23505", false)]
    [Arguments("23503", false)]
    [Arguments("22001", false)]
    [Arguments("42P01", false)]
    public async Task Only_a_transient_database_failure_counts_as_an_outage(string sqlState, bool outage)
    {
        // Act
        var failure = new InvalidOperationException(
            "An exception has been raised that is likely due to a transient failure.",
            new PostgresException("message", "ERROR", "ERROR", sqlState));

        // Assert
        await Assert.That(DatabaseOutage.Caused(failure)).IsEqualTo(outage);
    }

    [Test]
    public async Task An_outage_inside_an_aggregate_is_found()
    {
        // Act
        var failure = new AggregateException(
            new InvalidOperationException("first"),
            new NpgsqlException("Failed to connect", new SocketException((int)SocketError.ConnectionRefused)));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(DatabaseOutage.Caused(failure)).IsTrue();
            await Assert.That(DatabaseOutage.Caused(new AggregateException(new InvalidOperationException("only")))).IsFalse();
        }
    }
}
