package com.frappe.identity.infrastructure;

import com.frappe.identity.domain.BreachStatus;
import com.frappe.identity.domain.BreachedPasswords;
import com.frappe.identity.domain.Password;
import io.micrometer.observation.ObservationRegistry;
import java.net.http.HttpClient;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.HexFormat;
import org.springframework.http.HttpHeaders;
import org.springframework.http.client.JdkClientHttpRequestFactory;
import org.springframework.web.client.RestClient;
import org.springframework.web.client.RestClientException;

/**
 * The breach corpus of Have I Been Pwned's Pwned Passwords range API, by k-anonymity: only the first 5 hex characters
 * of the password's SHA-1 leave the process; the matching suffixes come back and are compared here. Responses are
 * padded ({@code Add-Padding}), so their size does not reveal the prefix either; padding entries have count 0.
 *
 * <p>A sanitizing adapter boundary that fails open: any failure (timeout, network, non-2xx, unreadable answer) is
 * {@link BreachStatus#UNKNOWN}, and is not logged. The HTTP client observation ({@code http.client.requests}) records
 * the request and its error; the password policy then accepts the password.
 */
final class HaveIBeenPwnedRestApiPasswordChecker implements BreachedPasswords {

    private static final int PREFIX_LENGTH = 5;

    private static final String USER_AGENT = "frappe-api";

    private static final HexFormat UPPERCASE_HEX = HexFormat.of().withUpperCase();

    private final RestClient rangeApi;

    /**
     * Creates the checker.
     *
     * @param properties where the range API answers and how long to wait for it
     * @param observations the registry the HTTP client observation is recorded in
     */
    HaveIBeenPwnedRestApiPasswordChecker(
            IdentityProperties.BreachedPasswordsProperties properties, ObservationRegistry observations) {
        var httpClient =
                HttpClient.newBuilder().connectTimeout(properties.timeout()).build();
        var requests = new JdkClientHttpRequestFactory(httpClient);
        requests.setReadTimeout(properties.timeout());
        this.rangeApi = RestClient.builder()
                .baseUrl(properties.baseUrl())
                .requestFactory(requests)
                .observationRegistry(observations)
                .defaultHeader(HttpHeaders.USER_AGENT, USER_AGENT)
                .build();
    }

    @Override
    public BreachStatus check(Password password) {
        var sha1 = sha1(password);
        var prefix = sha1.substring(0, PREFIX_LENGTH);
        var suffix = sha1.substring(PREFIX_LENGTH);
        try {
            var range = rangeApi.get()
                    .uri("/range/{prefix}", prefix)
                    .header("Add-Padding", "true")
                    .retrieve()
                    .body(String.class);
            return statusIn(range == null ? "" : range, suffix);
        } catch (RestClientException e) {
            // Fails open without logging: the client observation already carries the error.
            return BreachStatus.UNKNOWN;
        }
    }

    private static BreachStatus statusIn(String range, String suffix) {
        var entry = suffix + ":";
        for (var line : range.lines().toList()) {
            if (line.regionMatches(true, 0, entry, 0, entry.length())) {
                return occurrences(line.substring(entry.length())) > 0 ? BreachStatus.BREACHED : BreachStatus.NOT_FOUND;
            }
        }
        return BreachStatus.NOT_FOUND;
    }

    private static long occurrences(String count) {
        try {
            return Long.parseLong(count.strip());
        } catch (NumberFormatException e) {
            return 0;
        }
    }

    private static String sha1(Password password) {
        try {
            var digest =
                    MessageDigest.getInstance("SHA-1").digest(password.value().getBytes(StandardCharsets.UTF_8));
            return UPPERCASE_HEX.formatHex(digest);
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException("SHA-1 is required of every Java platform", e);
        }
    }
}
