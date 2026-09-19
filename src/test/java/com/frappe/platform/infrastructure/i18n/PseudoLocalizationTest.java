package com.frappe.platform.infrastructure.i18n;

import static org.assertj.core.api.Assertions.assertThat;

import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;

class PseudoLocalizationTest {

    @ParameterizedTest
    @CsvSource(
            delimiter = '|',
            quoteCharacter = '"',
            value = {
                "Hello, {0}! | [Ĥéĺĺó, {0}!]",
                "{0, plural, one {# item} other {# items}} | [{0, plural, one {# íŧéɱ} other {# íŧéɱš}}]",
                "{0, select, female {She} other {They}} left | [{0, select, female {Šĥé} other {Ŧĥéý}} ĺéƒŧ]",
                "Use '{'braces'}' now | [Úšé '{'ƀŕáçéš'}' ñóŵ]",
                "{0, number, integer} of {1} | [{0, number, integer} óƒ {1}]",
                "\"It''s {0}\" | \"[Íŧ''š {0}]\""
            })
    void accentsAndBracketsTheTextButKeepsTheIcuSyntax(String pattern, String expected) {
        // When / Then
        assertThat(PseudoLocalization.of(pattern)).isEqualTo(expected);
    }
}
