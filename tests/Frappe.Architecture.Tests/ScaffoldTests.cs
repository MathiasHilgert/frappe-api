using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Frappe.Testing;

namespace Frappe.Architecture.Tests;

/// <summary>
/// <c>just scaffold</c> writes the skeleton writers would otherwise type (eng/scaffold, eng/templates): each kind is run against a throwaway copy of the files it touches, so the real tree is never written.
/// That every skeleton compiles under the repository's analyzers was proven by scaffolding into a worktree and building it; here the files it writes and the appends it makes are pinned.
/// </summary>
public sealed class ScaffoldTests : IDisposable
{
    private const string Module = "src/Modules/Orders";

    /// <summary>The files the scaffold appends to or reads, as a module would have them: each text is the smallest shape the scaffold accepts.</summary>
    private static readonly Dictionary<string, string> Fixture = new()
    {
        [$"{Module}/Frappe.Orders.Domain/OrdersErrorCodes.cs"] = "namespace Frappe.Orders.Domain;\n\npublic static class OrdersErrorCodes\n{\n    public const string OrderTaken = \"orders.order.taken\";\n}\n",
        [$"{Module}/Frappe.Orders.Application/OrdersErrors.cs"] = "namespace Frappe.Orders.Application;\n\npublic static class OrdersErrors\n{\n    public static ConflictError OrderTaken { get; } = new(OrdersErrorCodes.OrderTaken);\n}\n",
        [$"{Module}/Frappe.Orders.Application/Orders/OrderAccess.cs"] = "namespace Frappe.Orders.Application.Orders;\n\ninternal sealed class OrderAccess\n{\n}\n",
        [$"{Module}/Frappe.Orders.Http/Resources/Messages/en.json"] = "{\n  \"orders.order.taken\": \"Taken.\"\n}\n",
        [$"{Module}/Frappe.Orders.Http/Resources/Messages/es.json"] = "{\n  \"orders.order.taken\": \"Tomado.\"\n}\n",
        [$"{Module}/Frappe.Orders.Http/Resources/Messages/pt-BR.json"] = "{\n  \"orders.order.taken\": \"Tomado.\"\n}\n",
        [$"{Module}/Frappe.Orders.Infrastructure/Messaging/OrdersIntegrationEvents.cs"] =
            "using PublishedOrderCancelled = Frappe.Orders.Contracts.Orders.OrderCancelled;\nusing PublishedRefunded = Frappe.Orders.Contracts.Orders.Refunded;\n\nnamespace Frappe.Orders.Infrastructure.Messaging;\n\ninternal static class OrdersIntegrationEvents\n{\n    public static object[] Translate(object domainEvent) => domainEvent switch\n    {\n        _ => [],\n    };\n}\n",
        ["tools/bruno/orders/cancel-order.yml"] = "info:\n  name: Cancel the order\n  type: http\n  seq: 1\n",
        ["tools/bruno/orders/place-order.yml"] = "info:\n  name: Place the order\n  type: http\n  seq: 2\n",
    };

    private readonly string _root = Directory.CreateTempSubdirectory("frappe-scaffold-").FullName;

    public ScaffoldTests()
    {
        foreach (var (relative, text) in Fixture)
        {
            var target = Path.Combine(_root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, text);
        }

        Directory.CreateDirectory(Path.Combine(_root, "tests/Modules/Orders/Frappe.Orders.UnitTests"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private async Task<string> ScaffoldAsync(params string[] arguments)
    {
        var (exitCode, output, error) = await RunAsync(arguments);
        await Assert.That(exitCode).IsEqualTo(0).Because(error);
        return output;
    }

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(string[] arguments)
    {
        var start = new ProcessStartInfo("/bin/bash") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(RepositoryRoot.FullPath, "eng/scaffold/scaffold.sh"));
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["SCAFFOLD_ROOT"] = _root;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output, await error);
    }

    private string Read(string relative) => File.ReadAllText(Path.Combine(_root, relative));

    [Test]
    public async Task A_command_gets_its_record_its_handler_and_a_failing_test_with_the_module_authorization_noted()
    {
        // Act
        await ScaffoldAsync("command", "Orders", "Probes", "ProbeThing");

        // Assert
        var command = Read("src/Modules/Orders/Frappe.Orders.Application/Probes/ProbeThing/ProbeThing.cs");
        var handler = Read("src/Modules/Orders/Frappe.Orders.Application/Probes/ProbeThing/ProbeThingHandler.cs");
        var test = Read("tests/Modules/Orders/Frappe.Orders.UnitTests/Probes/ProbeThingTests.cs");
        using (Assert.Multiple())
        {
            await Assert.That(command).Contains("namespace Frappe.Orders.Application.Probes.ProbeThing;")
                .And.Contains("public sealed record ProbeThing(Guid PrincipalId) : ICommand<Unit>;");
            await Assert.That(handler).Contains("internal sealed class ProbeThingHandler : ICommandHandler<ProbeThing, Unit>")
                .And.Contains("OrderAccess").And.Contains("NotImplementedException");
            await Assert.That(test).Contains("namespace Frappe.Orders.UnitTests.Probes;").And.Contains("new ProbeThingHandler()");
        }
    }

    [Test]
    public async Task A_query_gets_the_query_shapes()
    {
        // Act
        await ScaffoldAsync("query", "Orders", "Probes", "ReadProbe");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(Read("src/Modules/Orders/Frappe.Orders.Application/Probes/ReadProbe/ReadProbe.cs")).Contains(": IQuery<Unit>;");
            await Assert.That(Read("src/Modules/Orders/Frappe.Orders.Application/Probes/ReadProbe/ReadProbeHandler.cs")).Contains("IQueryHandler<ReadProbe, Unit>");
        }
    }

    [Test]
    public async Task An_error_is_appended_to_the_codes_the_errors_and_the_three_valid_message_files()
    {
        // Act
        await ScaffoldAsync("error", "Orders", "orders.probe.missing", "notfound", "Missing “{x}”.", "Falta «{x}».", "Faltando {x}.");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(Read("src/Modules/Orders/Frappe.Orders.Domain/OrdersErrorCodes.cs")).Contains("public const string ProbeMissing = \"orders.probe.missing\";");
            await Assert.That(Read("src/Modules/Orders/Frappe.Orders.Application/OrdersErrors.cs"))
                .Contains("public static NotFoundError ProbeMissing { get; } = new(OrdersErrorCodes.ProbeMissing);");
            foreach (var (language, text) in new[] { ("en", "Missing “{x}”."), ("es", "Falta «{x}»."), ("pt-BR", "Faltando {x}.") })
            {
                using var json = JsonDocument.Parse(Read($"src/Modules/Orders/Frappe.Orders.Http/Resources/Messages/{language}.json"));
                await Assert.That(json.RootElement.GetProperty("orders.probe.missing").GetString()).IsEqualTo(text);
                await Assert.That(json.RootElement.GetProperty("orders.order.taken").GetString()).IsNotNull();
            }
        }
    }

    [Test]
    public async Task An_event_gets_its_contract_its_domain_event_and_the_translator_arm_in_sorted_place()
    {
        // Act
        var output = await ScaffoldAsync("event", "Orders", "Orders/ProbeHappened", "OrderId:Guid,Name:string");

        // Assert
        var translator = Read("src/Modules/Orders/Frappe.Orders.Infrastructure/Messaging/OrdersIntegrationEvents.cs");
        using (Assert.Multiple())
        {
            await Assert.That(Read("src/Modules/Orders/Frappe.Orders.Contracts/Orders/ProbeHappened.cs"))
                .Contains("[IntegrationEvent(\"frappe.orders.probe_happened.v1\")]")
                .And.Contains("public sealed record ProbeHappened(Guid OrderId, string Name, DateTimeOffset OccurredAt) : IIntegrationEvent;");
            await Assert.That(Read("src/Modules/Orders/Frappe.Orders.Domain/Orders/ProbeHappened.cs"))
                .Contains("public sealed record ProbeHappened(Guid OrderId, string Name, Instant At) : IDomainEvent;");
            await Assert.That(translator).Contains("ProbeHappened e => [new PublishedProbeHappened(e.OrderId, e.Name, e.At.ToDateTimeOffset())],");
            await Assert.That(translator.IndexOf("using PublishedProbeHappened", StringComparison.Ordinal)).IsBetween(
                translator.IndexOf("using PublishedOrderCancelled", StringComparison.Ordinal), translator.IndexOf("using PublishedRefunded", StringComparison.Ordinal));
            await Assert.That(output).Contains("frappe.orders.probe_happened.v1");
        }
    }

    [Test]
    public async Task An_endpoint_gets_its_mapping_and_a_bruno_request_the_next_free_seq()
    {
        // Arrange
        var maximum = Fixture.Keys.Where(path => path.StartsWith("tools/bruno/", StringComparison.Ordinal))
            .Max(path => int.Parse(File.ReadLines(Path.Combine(_root, path)).First(line => line.StartsWith("  seq:", StringComparison.Ordinal))[6..], CultureInfo.InvariantCulture));

        await ScaffoldAsync("command", "Orders", "Probes", "ProbeThing");

        // Act
        await ScaffoldAsync("endpoint", "Orders", "Probes", "ProbeThing", "post", "/probes");
        await ScaffoldAsync("bruno", "orders", "Probe it", "post", "/v1/probes", "204");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(Read("src/Modules/Orders/Frappe.Orders.Http/Probes/ProbeThingEndpoints.cs"))
                .Contains("group.MapPost(\"/probes\", HandleAsync)").And.Contains("public void MapProbeThingEndpoints()");
            var request = Read("tools/bruno/orders/probe-it.yml");
            await Assert.That(request).Contains($"  seq: {maximum + 1}").And.Contains("method: POST").And.Contains("url: \"{{baseUrl}}/v1/probes\"").And.Contains("value: \"204\"");
        }
    }

    [Test]
    public async Task An_endpoint_without_its_use_case_is_refused_and_writes_nothing()
    {
        // Act
        var (exitCode, _, error) = await RunAsync(["endpoint", "Orders", "Probes", "Missing", "get", "/probes"]);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsNotEqualTo(0);
            await Assert.That(error).Contains("no use case Orders/Probes/Missing");
            await Assert.That(File.Exists(Path.Combine(_root, "src/Modules/Orders/Frappe.Orders.Http/Probes/MissingEndpoints.cs"))).IsFalse();
        }
    }

    [Test]
    public async Task An_endpoint_of_a_query_uses_the_query_handler_and_answers_200()
    {
        // Arrange
        await ScaffoldAsync("query", "Orders", "Probes", "ReadProbe");

        // Act
        await ScaffoldAsync("endpoint", "Orders", "Probes", "ReadProbe", "get", "/probes");

        // Assert
        var endpoint = Read("src/Modules/Orders/Frappe.Orders.Http/Probes/ReadProbeEndpoints.cs");
        using (Assert.Multiple())
        {
            await Assert.That(endpoint).Contains("IQueryHandler<ReadProbe, Unit> handler").And.Contains("Results<Ok<Unit>, ErrorsProblemHttpResult>")
                .And.Contains("TypedResults.Ok(result)").And.DoesNotContain("ICommandHandler").And.DoesNotContain("NoContent");
        }
    }

    [Test]
    public async Task An_error_with_an_invalid_message_file_changes_no_file()
    {
        // Arrange
        string[] touched =
        [
            "src/Modules/Orders/Frappe.Orders.Domain/OrdersErrorCodes.cs",
            "src/Modules/Orders/Frappe.Orders.Application/OrdersErrors.cs",
            "src/Modules/Orders/Frappe.Orders.Http/Resources/Messages/en.json",
            "src/Modules/Orders/Frappe.Orders.Http/Resources/Messages/es.json",
            "src/Modules/Orders/Frappe.Orders.Http/Resources/Messages/pt-BR.json",
        ];
        await File.WriteAllTextAsync(Path.Combine(_root, touched[3]), "{ not json");
        var before = touched.Select(Read).ToArray();

        // Act
        var (exitCode, _, error) = await RunAsync(["error", "Orders", "orders.probe.missing", "notfound", "Missing.", "Falta.", "Faltando."]);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsNotEqualTo(0);
            await Assert.That(error).Contains("es.json is not valid JSON");
            await Assert.That(touched.Select(Read).ToArray()).IsEquivalentTo(before);
        }
    }

    [Test]
    public async Task An_error_whose_errors_file_cannot_take_the_append_changes_no_file()
    {
        // Arrange
        string[] touched =
        [
            "src/Modules/Orders/Frappe.Orders.Domain/OrdersErrorCodes.cs",
            "src/Modules/Orders/Frappe.Orders.Application/OrdersErrors.cs",
            "src/Modules/Orders/Frappe.Orders.Http/Resources/Messages/en.json",
        ];
        await File.AppendAllTextAsync(Path.Combine(_root, touched[1]), "// trailing\n");
        var before = touched.Select(Read).ToArray();

        // Act
        var (exitCode, _, _) = await RunAsync(["error", "Orders", "orders.probe.missing", "notfound", "Missing.", "Falta.", "Faltando."]);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsNotEqualTo(0);
            await Assert.That(touched.Select(Read).ToArray()).IsEquivalentTo(before);
        }
    }
}
