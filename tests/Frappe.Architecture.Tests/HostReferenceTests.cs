using System.Reflection;

namespace Frappe.Architecture.Tests;

/// <summary>
/// What each host is allowed to reach: the Migrator is a one-shot database tool and never serves HTTP, so ASP.NET Core stays out of
/// its image; the hosts never reference one another. Checked on the compiled assemblies of each host and everything of ours it loads.
/// </summary>
public sealed class HostReferenceTests
{
    private static readonly Assembly Api = typeof(Frappe.Api.ApiComposition).Assembly;
    private static readonly Assembly Migrator = typeof(Frappe.Migrator.MigratorComposition).Assembly;
    private static readonly Assembly Worker = typeof(Frappe.Worker.WorkerComposition).Assembly;

    /// <summary>The names of every <c>Frappe.*</c> assembly <paramref name="host"/> reaches, itself included, and what each references.</summary>
    private static Dictionary<string, string[]> Closure(Assembly host)
    {
        var closure = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var pending = new Stack<Assembly>([host]);
        while (pending.TryPop(out var assembly))
        {
            var name = assembly.GetName().Name!;
            if (closure.ContainsKey(name))
            {
                continue;
            }

            var references = assembly.GetReferencedAssemblies();
            closure[name] = [.. references.Select(reference => reference.Name!)];
            foreach (var reference in references.Where(reference => reference.Name!.StartsWith("Frappe.", StringComparison.Ordinal)))
            {
                pending.Push(System.Reflection.Assembly.Load(reference));
            }
        }

        return closure;
    }

    private static string[] AspNetCoreReferences(Assembly host) =>
        [.. Closure(host)
            .SelectMany(entry => entry.Value.Where(reference => reference.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
                .Select(reference => $"{entry.Key} -> {reference}"))
            .Order(StringComparer.Ordinal)];

    [Test]
    public async Task The_Migrator_and_everything_it_loads_reference_no_ASP_NET_Core()
    {
        // Act
        var offenders = AspNetCoreReferences(Migrator);

        // Assert
        await Assert.That(offenders).IsEmpty().Because("the Migrator runs no web server: " + string.Join(", ", offenders));
    }

    [Test]
    public async Task The_Migrator_reaches_neither_the_web_stack_the_broker_nor_the_cache()
    {
        // Act
        var closure = Closure(Migrator).Keys;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(closure).DoesNotContain("Frappe.Http");
            await Assert.That(closure).DoesNotContain("Frappe.Caching");
            await Assert.That(closure).DoesNotContain("Frappe.Messaging");
            await Assert.That(closure).DoesNotContain("Frappe.Api");
            await Assert.That(closure).DoesNotContain("Frappe.Worker");
        }
    }

    [Test]
    public async Task The_Migrator_may_reach_the_messaging_abstractions_and_storage_but_never_the_broker_side_of_messaging()
    {
        // Act
        var messaging = Closure(Migrator).Keys.Where(name => name.StartsWith("Frappe.Messaging", StringComparison.Ordinal)).ToArray();

        // Assert
        // Storage (outbox and inbox model, relay roles) is how a module's database module maps its tables for the Migrator; the
        // abstractions are how a module declares its relay and consumers without the broker.
        var offenders = messaging.Except(["Frappe.Messaging.Storage", "Frappe.Messaging.Abstractions"]).ToArray();
        await Assert.That(offenders).IsEmpty().Because("only the abstractions and the storage are allowed: " + string.Join(", ", offenders));
    }

    [Test]
    public async Task The_messaging_storage_references_neither_the_broker_client_nor_ASP_NET_Core()
    {
        // Act
        var closure = Closure(typeof(Frappe.Messaging.Storage.Relay.RelayRoles).Assembly);

        // Assert
        var offenders = closure.SelectMany(entry => entry.Value
                .Where(reference => reference.StartsWith("NATS", StringComparison.Ordinal)
                    || reference.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
                .Select(reference => $"{entry.Key} -> {reference}"))
            .ToArray();
        using (Assert.Multiple())
        {
            await Assert.That(offenders).IsEmpty().Because(string.Join(", ", offenders));
            await Assert.That(closure.Keys).DoesNotContain("Frappe.Messaging");
        }
    }

    [Test]
    public async Task The_messaging_abstractions_reference_neither_the_broker_client_nor_ASP_NET_Core()
    {
        // Act
        var closure = Closure(typeof(Frappe.Messaging.Abstractions.MessagingDeclarations).Assembly);

        // Assert
        var offenders = closure.SelectMany(entry => entry.Value
                .Where(reference => reference.StartsWith("NATS", StringComparison.Ordinal)
                    || reference.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
                .Select(reference => $"{entry.Key} -> {reference}"))
            .ToArray();
        using (Assert.Multiple())
        {
            await Assert.That(offenders).IsEmpty().Because(string.Join(", ", offenders));
            await Assert.That(closure.Keys).DoesNotContain("Frappe.Messaging");
        }
    }

    [Test]
    public async Task The_Migrator_loads_the_notifications_database_but_neither_its_web_layer_nor_its_email_or_content_plug_ins()
    {
        // Act
        var closure = Closure(Migrator).Keys;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(closure).Contains("Frappe.Notifications.Infrastructure");
            await Assert.That(closure).DoesNotContain("Frappe.Notifications.Http");
            await Assert.That(closure.Where(name => name.StartsWith("Frappe.Notifications.Channels", StringComparison.Ordinal))).IsEmpty();
            await Assert.That(closure.Where(name => name.StartsWith("Frappe.Notifications.Kinds", StringComparison.Ordinal))).IsEmpty();
        }
    }

    [Test]
    public async Task The_Migrator_loads_the_identity_database_but_neither_its_web_layer_nor_its_valkey_limits()
    {
        // Act
        var closure = Closure(Migrator).Keys;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(closure).Contains("Frappe.Identity.Infrastructure");
            await Assert.That(closure).DoesNotContain("Frappe.Identity.Http");
            await Assert.That(closure).DoesNotContain("Frappe.Identity.Limits.Valkey");
        }
    }

    [Test]
    public async Task The_Worker_runs_identity_without_its_web_layer_and_without_the_valkey_limits_only_the_Api_has()
    {
        // Act
        var closure = Closure(Worker).Keys;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(closure).Contains("Frappe.Identity.Infrastructure");
            await Assert.That(closure).DoesNotContain("Frappe.Identity.Http");
            await Assert.That(closure).DoesNotContain("Frappe.Identity.Limits.Valkey");
        }
    }

    [Test]
    public async Task The_Api_serves_identity_with_its_web_layer_and_its_valkey_limits()
    {
        // Act
        var closure = Closure(Api).Keys;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(closure).Contains("Frappe.Identity.Infrastructure");
            await Assert.That(closure).Contains("Frappe.Identity.Http");
            await Assert.That(closure).Contains("Frappe.Identity.Limits.Valkey");
        }
    }

    [Test]
    public async Task The_Api_and_the_Worker_reference_no_other_host_and_not_the_Migrator()
    {
        // Act
        var api = Closure(Api).Keys;
        var worker = Closure(Worker).Keys;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(api).DoesNotContain("Frappe.Migrator").And.DoesNotContain("Frappe.Worker");
            await Assert.That(worker).DoesNotContain("Frappe.Migrator").And.DoesNotContain("Frappe.Api");
        }
    }

    [Test]
    public async Task The_rule_can_see_ASP_NET_Core_where_it_is_used()
    {
        // Act
        // Positive control: a rule that can never fail would prove nothing. The Api serves HTTP, so it must show up.
        var offenders = AspNetCoreReferences(Api);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(offenders).IsNotEmpty();
            await Assert.That(offenders.Any(offender => offender.StartsWith("Frappe.Http ->", StringComparison.Ordinal))).IsTrue();
        }
    }
}
