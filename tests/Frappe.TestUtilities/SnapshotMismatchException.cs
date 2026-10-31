namespace Frappe.TestUtilities;

/// <summary>A JSON contract differs from its committed snapshot.</summary>
/// <param name="message">What differs and where the received file is.</param>
public sealed class SnapshotMismatchException(string message) : Exception(message);
