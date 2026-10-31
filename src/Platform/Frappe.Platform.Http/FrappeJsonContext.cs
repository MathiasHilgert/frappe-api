using System.Text.Json.Serialization;
using Frappe.Platform.Http.Problems;

namespace Frappe.Platform.Http;

/// <summary>Source-generated metadata for the platform types written on the wire; each module chains its own.</summary>
[JsonSerializable(typeof(ProblemError[]))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class FrappeJsonContext : JsonSerializerContext;
