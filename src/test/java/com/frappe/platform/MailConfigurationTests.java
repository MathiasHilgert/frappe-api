package com.frappe.platform;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.frappe.FrappeApiApplication;
import org.assertj.core.util.Throwables;
import org.junit.jupiter.api.Test;
import org.springframework.boot.WebApplicationType;
import org.springframework.boot.builder.SpringApplicationBuilder;

/** Outside the local profile mail goes through Resend, so startup needs its API key and a sender address. */
class MailConfigurationTests {

    @Test
    void startupOutsideLocalProfileFailsWithoutTheResendApiKey() {
        // Given every other setting, but no Resend API key
        var app = application("FRAPPE_MAIL_FROM=Frappé <no-reply@frappe.test>");

        // Then
        assertThatThrownBy(() -> app.run())
                .hasStackTraceContaining("MissingMailSettingsException")
                .hasStackTraceContaining("RESEND_API_KEY");
    }

    @Test
    void startupOutsideLocalProfileFailsWithoutTheSenderAddress() {
        // Given every other setting, but no sender
        var app = application("RESEND_API_KEY=re_not_a_real_key");

        // Then
        assertThatThrownBy(() -> app.run())
                .hasStackTraceContaining("MissingMailSettingsException")
                .hasStackTraceContaining("FRAPPE_MAIL_FROM");
    }

    @Test
    void smtpNeedsNoResendApiKey() {
        // Given SMTP instead of Resend; startup then fails later, on the unreachable database
        var app = application("FRAPPE_MAIL_FROM=Frappé <no-reply@frappe.test>", "frappe.mail.provider=smtp");

        // Then
        assertThatThrownBy(() -> app.run())
                .satisfies(failure ->
                        assertThat(Throwables.getStackTrace(failure)).doesNotContain("MissingMailSettingsException"));
    }

    private static SpringApplicationBuilder application(String... mailSettings) {
        return new SpringApplicationBuilder(FrappeApiApplication.class)
                .web(WebApplicationType.NONE)
                .properties(
                        "spring.docker.compose.enabled=false",
                        "FRAPPE_DB_URL=jdbc:postgresql://localhost:1/frappe",
                        "FRAPPE_APP_PASSWORD=unused",
                        "FRAPPE_OWNER_PASSWORD=unused",
                        "FRAPPE_VALKEY_URL=redis://localhost:1",
                        "FRAPPE_SECRET_PEPPER=unused-but-present-0123456789abcdef")
                .properties(mailSettings);
    }
}
