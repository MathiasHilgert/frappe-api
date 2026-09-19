package com.frappe.platform.infrastructure.mail;

import static org.assertj.core.api.Assertions.assertThat;

import org.junit.jupiter.api.Test;

class PlainTextAlternativeTest {

    @Test
    void separatesBlocksByABlankLineAndKeepsLineBreaks() {
        // When
        var text = PlainTextAlternative.of("<h1>Tu código</h1><p>Hola,<br>Ana</p><p>  Código:   <b>123456</b> </p>");

        // Then
        assertThat(text).isEqualTo("Tu código\n\nHola,\nAna\n\nCódigo: 123456");
    }

    @Test
    void writesLinksWithTheirAddress() {
        // When
        var text = PlainTextAlternative.of(
                "<p>Open <a href=\"https://frappe.test/verify?c=1&amp;d=2\">this link</a>.</p>");

        // Then
        assertThat(text).isEqualTo("Open this link (https://frappe.test/verify?c=1&d=2).");
    }

    @Test
    void writesListItemsOnTheirOwnLines() {
        // When
        var text = PlainTextAlternative.of("<ul><li>One</li><li>Two</li></ul>");

        // Then
        assertThat(text).isEqualTo("- One\n- Two");
    }

    @Test
    void decodesEntitiesAndDropsMarkup() {
        // When
        var text = PlainTextAlternative.of("<p>Tom &amp; Jerry &lt;3</p><style>p{}</style>");

        // Then
        assertThat(text).isEqualTo("Tom & Jerry <3");
    }
}
