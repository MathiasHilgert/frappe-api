package com.frappe.platform.infrastructure.i18n;

import com.ibm.icu.text.MessagePattern;
import java.util.EnumSet;
import java.util.Set;

/**
 * Turns an English ICU pattern into its en-XA pseudo-localization: every ASCII letter of the translatable text is
 * replaced by an accented look-alike and the whole message is bracketed ({@code Hello, {0}!} becomes
 * {@code [Ĥéĺĺó, {0}!]}). Tests and reviewers use it to spot text that bypasses the catalogs (it stays plain) and text
 * that is cut or concatenated (a bracket goes missing).
 *
 * <p>Only literal message text changes; argument names, types, styles, plural and select keywords stay intact, so the
 * result is a valid pattern with the same arguments.
 */
final class PseudoLocalization {

    private static final String LOWER = "áƀçđéƒĝĥíĵķĺɱñóþǫŕšŧúṽŵẋýž";
    private static final String UPPER = "ÁƁÇĐÉƑĜĤÍĴĶĹṀÑÓÞǪŔŠŦÚṼŴẊÝŽ";

    // Literal message text follows these parts and runs up to the next part; everything else is argument syntax.
    private static final Set<MessagePattern.Part.Type> TEXT_FOLLOWS = EnumSet.of(
            MessagePattern.Part.Type.MSG_START,
            MessagePattern.Part.Type.ARG_LIMIT,
            MessagePattern.Part.Type.SKIP_SYNTAX,
            MessagePattern.Part.Type.INSERT_CHAR,
            MessagePattern.Part.Type.REPLACE_NUMBER);

    private PseudoLocalization() {}

    /**
     * Pseudo-localizes an ICU message pattern.
     *
     * @param pattern a valid ICU MessageFormat pattern
     * @return the accented, bracketed pattern
     * @throws IllegalArgumentException if the pattern is not valid ICU syntax
     */
    static String of(String pattern) {
        var parts = new MessagePattern(pattern);
        var result = new StringBuilder(pattern.length() + 2).append('[');
        var copied = 0;
        var inText = false;
        for (var i = 0; i < parts.countParts(); i++) {
            var part = parts.getPart(i);
            append(result, pattern, copied, part.getIndex(), inText);
            append(result, pattern, part.getIndex(), part.getLimit(), false);
            copied = part.getLimit();
            inText = TEXT_FOLLOWS.contains(part.getType());
        }
        append(result, pattern, copied, pattern.length(), inText);
        return result.append(']').toString();
    }

    private static void append(StringBuilder result, String pattern, int from, int to, boolean accent) {
        for (var i = from; i < to; i++) {
            var character = pattern.charAt(i);
            result.append(accent ? accented(character) : character);
        }
    }

    private static char accented(char character) {
        if (character >= 'a' && character <= 'z') {
            return LOWER.charAt(character - 'a');
        }
        if (character >= 'A' && character <= 'Z') {
            return UPPER.charAt(character - 'A');
        }
        return character;
    }
}
