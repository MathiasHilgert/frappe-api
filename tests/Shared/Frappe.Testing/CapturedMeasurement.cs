namespace Frappe.Testing;

/// <summary>One measurement of an instrument, with its tags.</summary>
public sealed record CapturedMeasurement(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags);
