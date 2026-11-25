using System.Diagnostics.Metrics;

namespace Frappe.Architecture.Tests;

/// <summary>
/// The meter convention (OBS-08): every instrument a module creates on its <c>Frappe.&lt;M&gt;</c> meter is named <c>frappe.&lt;module&gt;.*</c>, has
/// a unit and a description, and its name constant lives in the module's <c>Application/Telemetry</c> folder. The metrics classes are built with a
/// recording meter factory and the instruments they publish are read back.
/// </summary>
public sealed class MeterConventionTests
{
    private static readonly string[] Modules = ModuleDoorTests.AllModules;

    private static readonly Type[] MetricsTypes =
    [
        .. ArchitectureRules.ModuleAssemblies(ArchitectureRules.ProductionGraph.Keys, Modules)
            .SelectMany(name => System.Reflection.Assembly.Load(name).GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && BuildsFromFactory(type)),
    ];

    // Meters are read back from the module's own metrics classes, whose fields are private by design.
#pragma warning disable S3011
    private const System.Reflection.BindingFlags Flags =
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

    /// <summary>An instrument as read back from a meter.</summary>
    private sealed record Fact(string Meter, string Name, string? Unit, string? Description, Type Owner);

    private static bool BuildsFromFactory(Type type) =>
        type.GetConstructors(Flags)
            .Any(ctor => ctor.GetParameters() is [{ ParameterType: var only }] && only == typeof(IMeterFactory));

    /// <summary>Builds each type with a factory that records its meters, and returns what its instruments declare.</summary>
    private static Fact[] Collect(IEnumerable<Type> types)
    {
        var facts = new List<Fact>();
        foreach (var type in types)
        {
            using var factory = new RecordingMeterFactory();
            using var listener = new MeterListener
            {
                InstrumentPublished = (instrument, _) =>
                {
                    if (factory.Owns(instrument.Meter))
                    {
                        facts.Add(new Fact(instrument.Meter.Name, instrument.Name, instrument.Unit, instrument.Description, type));
                    }
                },
            };
            listener.Start();
            _ = Activator.CreateInstance(
                type,
                Flags,
                binder: null,
                args: [factory],
                culture: null);
        }

        return [.. facts];
    }

    /// <summary>The offenses of the instruments on a <c>Frappe.&lt;M&gt;</c> meter, one line each.</summary>
    private static string[] Offenders(IEnumerable<Fact> facts) =>
    [
        .. facts
            .Select(fact => (Fact: fact, Module: Modules.FirstOrDefault(module => fact.Meter == $"Frappe.{module}")))
            .Where(pair => pair.Module is not null)
            .SelectMany(pair => Problems(pair.Fact, pair.Module!).Select(problem => $"{pair.Fact.Name}: {problem}"))
            .Order(StringComparer.Ordinal),
    ];

    private static IEnumerable<string> Problems(Fact fact, string module)
    {
        if (!fact.Name.StartsWith($"frappe.{module.ToLowerInvariant()}.", StringComparison.Ordinal))
        {
            yield return $"name is not frappe.{module.ToLowerInvariant()}.*";
        }

        if (string.IsNullOrWhiteSpace(fact.Unit))
        {
            yield return "no unit";
        }

        if (string.IsNullOrWhiteSpace(fact.Description))
        {
            yield return "no description";
        }

        if (!NameConstantLivesInTelemetry(fact.Name, module))
        {
            yield return $"no name constant in Frappe.{module}.Application.Telemetry";
        }
    }

    private static bool NameConstantLivesInTelemetry(string name, string module) =>
        System.Reflection.Assembly.Load($"Frappe.{module}.Application").GetTypes()
            .Where(type => (type.Namespace ?? string.Empty) == $"Frappe.{module}.Application.Telemetry")
            .SelectMany(type => type.GetFields(
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            .Any(field => field.IsLiteral && field.GetRawConstantValue() is string value && value == name);

    [Test]
    public async Task Every_instrument_on_a_module_meter_follows_the_convention()
    {
        // Act
        var facts = Collect(MetricsTypes);
        var offenders = Offenders(facts);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(facts.Select(fact => fact.Meter).Distinct()).IsEquivalentTo(Modules.Select(module => $"Frappe.{module}"));
            await Assert.That(offenders).IsEmpty().Because($"OBS-08: {string.Join("; ", offenders)}");
        }
    }

    [Test]
    public async Task The_meter_rule_can_see_an_instrument_that_breaks_the_convention()
    {
        // Act
        var offenders = Offenders(Collect([typeof(SloppyMetrics)]));

        // Assert
        await Assert.That(offenders).IsEquivalentTo(
        [
            "sloppy: name is not frappe.identity.*",
            "sloppy: no unit",
            "sloppy: no description",
            "sloppy: no name constant in Frappe.Identity.Application.Telemetry",
        ]);
    }

    /// <summary>An instrument on the Identity meter with a stray name, no unit and no description.</summary>
    internal sealed class SloppyMetrics
    {
        public SloppyMetrics(IMeterFactory meters) => meters.Create("Frappe.Identity").CreateCounter<long>("sloppy");
    }

    /// <summary>A meter factory that keeps the meters it creates, to read their instruments back.</summary>
    private sealed class RecordingMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = [];

        public bool Owns(Meter meter) => _meters.Contains(meter);

        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(options.Name, options.Version, options.Tags, scope: this);
            _meters.Add(meter);
            return meter;
        }

        public void Dispose()
        {
            foreach (var meter in _meters)
            {
                meter.Dispose();
            }
        }
    }
}
