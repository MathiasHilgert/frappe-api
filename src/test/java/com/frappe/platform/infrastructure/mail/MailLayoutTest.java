package com.frappe.platform.infrastructure.mail;

import static org.assertj.core.api.Assertions.assertThat;

import gg.jte.ContentType;
import gg.jte.TemplateEngine;
import gg.jte.output.StringOutput;
import java.util.Map;
import org.junit.jupiter.api.Test;

/** The MJML layout is compiled to HTML at build time and precompiled by JTE together with the mail templates. */
class MailLayoutTest {

    private final TemplateEngine templates = TemplateEngine.createPrecompiled(ContentType.Html);

    @Test
    void theCompiledLayoutWrapsTheBodyInResponsiveHtml() {
        // Given
        var output = new StringOutput();
        gg.jte.Content body = content -> content.writeContent("<p>Hola</p>");

        // When
        templates.render("mail/layout.jte", Map.of("lang", "es", "subject", "Tu código & más", "body", body), output);

        // Then MJML produced the table layout, and JTE escaped the subject but not the rendered body
        var html = output.toString();
        assertThat(html)
                .startsWith("<!doctype html>")
                .contains("<html lang=\"es\"", "<title>Tu código &amp; más</title>", "<p>Hola</p>")
                .contains("<table")
                .doesNotContain("<mj-", "${");
    }
}
