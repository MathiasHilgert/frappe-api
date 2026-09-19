package com.frappe.identity.infrastructure.web;

import com.frappe.identity.application.CompleteSignUp;
import com.frappe.identity.domain.EmailAddress;
import com.frappe.platform.web.Access;
import com.frappe.platform.web.Posture;
import com.frappe.platform.web.RequestRefusedException;
import io.swagger.v3.oas.annotations.Operation;
import io.swagger.v3.oas.annotations.media.Schema;
import io.swagger.v3.oas.annotations.responses.ApiResponse;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.validation.Valid;
import jakarta.validation.constraints.NotNull;
import java.net.InetAddress;
import java.net.URI;
import java.util.List;
import java.util.Locale;
import java.util.UUID;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RestController;

/**
 * {@code POST /v1/sign-ups/completion}: the holder of a sign-up code becomes an active person and gets their recovery
 * codes, the only time they are shown. The client signs in right after.
 */
@RestController
@Access(Posture.PUBLIC)
class CompleteSignUpRoute {

    private static final URI ME = URI.create("/v1/me");

    /**
     * The request body.
     *
     * @param email the address the code was mailed to
     * @param code the code from the mail
     * @param password the chosen password
     * @param givenName the given name
     * @param familyName the family name
     * @param termsVersion the terms of service version the person accepted
     * @param privacyVersion the privacy policy version the person accepted
     */
    record Request(
            @Schema(format = "email", maxLength = 254) @NotNull @Email
            String email,

            @NotNull String code,

            @Schema(format = "password", minLength = 15, maxLength = 128) @NotNull
            String password,

            @Schema(maxLength = 80) @NotNull String givenName,
            @Schema(maxLength = 80) @NotNull String familyName,
            @NotNull String termsVersion,
            @NotNull String privacyVersion) {

        @Override
        public String toString() {
            return "Request[<redacted>]";
        }
    }

    /**
     * The answer.
     *
     * @param personId the new person's id
     * @param recoveryCodes the recovery codes, shown only now
     */
    record Response(UUID personId, List<String> recoveryCodes) {

        @Override
        public String toString() {
            return "Response[personId=" + personId + ", recoveryCodes=<redacted>]";
        }
    }

    private final CompleteSignUp completeSignUp;

    /**
     * Creates the route.
     *
     * @param completeSignUp the use case
     */
    CompleteSignUpRoute(CompleteSignUp completeSignUp) {
        this.completeSignUp = completeSignUp;
    }

    /**
     * Completes the sign-up.
     *
     * @param request the code, password, names and accepted legal versions
     * @param locale the language the request resolved to, the person's preferred language
     * @param servletRequest the request, for the client address
     * @return 201 with the person id and the recovery codes
     */
    @PostMapping("/sign-ups/completion")
    @Operation(summary = "Complete a sign-up and receive the recovery codes, shown once")
    @ApiResponse(responseCode = "201", description = "The person exists; Location is /v1/me")
    @ApiResponse(
            responseCode = "400",
            description = "invalid-code: wrong, expired, used or never issued; invalid-request: a field is missing")
    @ApiResponse(
            responseCode = "409",
            description = "legal-terms-outdated: not the current versions; email-already-registered: lost a race")
    @ApiResponse(
            responseCode = "422",
            description = "password-rejected (params.reason too_short, too_long, breached); invalid-name")
    @ApiResponse(responseCode = "429", description = "too-many-attempts: the address or client address is rate limited")
    ResponseEntity<Response> complete(
            @Valid @RequestBody Request request, Locale locale, HttpServletRequest servletRequest) {
        // A literal parse, never DNS: Tomcat already resolved the trusted-proxy chain into the remote address.
        var clientAddress = InetAddress.ofLiteral(servletRequest.getRemoteAddr());
        var email = EmailAddress.of(request.email())
                .orElseThrow(rejected -> new IllegalStateException("A validated address was refused: " + rejected));
        var registration = completeSignUp
                .complete(
                        new CompleteSignUp.Request(
                                email,
                                request.code(),
                                request.password(),
                                request.givenName(),
                                request.familyName(),
                                request.termsVersion(),
                                request.privacyVersion()),
                        clientAddress,
                        locale)
                .orElseThrow(RequestRefusedException::new);
        return ResponseEntity.created(ME).body(new Response(registration.personId(), registration.recoveryCodes()));
    }
}
