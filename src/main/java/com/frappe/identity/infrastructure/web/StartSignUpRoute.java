package com.frappe.identity.infrastructure.web;

import com.frappe.identity.application.StartSignUp;
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
import java.util.Locale;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RestController;

/**
 * {@code POST /v1/sign-ups}: starts a sign-up and mails a code to the address. Answers 202 alike for every address, so
 * the answer never reveals whether it is registered or capped.
 */
@RestController
@Access(Posture.PUBLIC)
class StartSignUpRoute {

    /**
     * The request body.
     *
     * @param email the address to sign up with
     */
    record Request(
            @Schema(format = "email", maxLength = 254) @NotNull @Email
            String email) {}

    private final StartSignUp startSignUp;

    /**
     * Creates the route.
     *
     * @param startSignUp the use case
     */
    StartSignUpRoute(StartSignUp startSignUp) {
        this.startSignUp = startSignUp;
    }

    /**
     * Starts the sign-up.
     *
     * @param request the address
     * @param locale the language the request resolved to, for the mail
     * @param servletRequest the request, for the client address
     * @return 202 without a body
     */
    @PostMapping("/sign-ups")
    @Operation(summary = "Start a sign-up by emailing a code")
    @ApiResponse(responseCode = "202", description = "Accepted; a code is mailed unless the address is capped")
    @ApiResponse(
            responseCode = "400",
            description =
                    "invalid-request: errors[].code not-null (no address) or errors[].code email (not an address)")
    @ApiResponse(responseCode = "429", description = "too-many-attempts: the client address is rate limited")
    ResponseEntity<Void> start(@Valid @RequestBody Request request, Locale locale, HttpServletRequest servletRequest) {
        // A literal parse, never DNS: Tomcat already resolved the trusted-proxy chain into the remote address.
        var clientAddress = InetAddress.ofLiteral(servletRequest.getRemoteAddr());
        var email = EmailAddress.of(request.email())
                .orElseThrow(rejected -> new IllegalStateException("A validated address was refused: " + rejected));
        startSignUp.start(email, clientAddress, locale).orElseThrow(RequestRefusedException::new);
        return ResponseEntity.accepted().build();
    }
}
