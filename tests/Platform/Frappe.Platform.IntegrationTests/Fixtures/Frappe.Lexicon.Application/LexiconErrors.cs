namespace Frappe.Lexicon.Application;

/// <summary>The fake module's error catalog.</summary>
public static class LexiconErrors
{
    /// <summary>Gets the error for a word the lexicon does not know.</summary>
    public static Error UnknownWord { get; } =
        Error.NotFound("lexicon.word.unknown", "The word is unknown.");
}
