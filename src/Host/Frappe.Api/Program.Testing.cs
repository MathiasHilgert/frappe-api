/// <summary>Exposed for integration tests.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1118:Utility classes should not have public constructors",
    Justification = "Test hosts use Program as a generic type argument, so it cannot be static.")]
public partial class Program;
