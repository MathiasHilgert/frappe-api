package com.frappe.platform.infrastructure.web;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.web.Access;
import com.frappe.platform.web.Posture;
import com.frappe.platform.web.ResolvedSession;
import com.frappe.platform.web.SessionKind;
import com.frappe.platform.web.SessionResolver;
import jakarta.servlet.http.Cookie;
import java.util.Map;
import java.util.Optional;
import java.util.UUID;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.EnumSource;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.context.ApplicationContext;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Import;
import org.springframework.http.HttpHeaders;
import org.springframework.http.HttpStatus;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.security.core.userdetails.UserDetailsService;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.assertj.MockMvcTester;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RestController;

/** Every route's posture is enforced before its controller runs, from the {@code Authorization: Bearer} header only. */
@SpringBootTest
@AutoConfigureMockMvc
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class, RouteAccessTests.Routes.class})
@ActiveProfiles("local")
class RouteAccessTests {

    static final UUID PERSON_ID = UUID.fromString("0190a8f0-0000-7000-8000-000000000001");
    static final Map<String, ResolvedSession> SESSIONS = Map.of(
            "person-token", session(PERSON_ID, SessionKind.PERSON),
            "terminal-token", session(UUID.randomUUID(), SessionKind.TERMINAL),
            "guest-token", session(UUID.randomUUID(), SessionKind.GUEST));

    @TestConfiguration(proxyBeanMethods = false)
    static class Routes {

        @Bean
        AtomicInteger controllerCalls() {
            return new AtomicInteger();
        }

        @Bean
        SessionResolver sessionResolver() {
            return token -> Optional.ofNullable(SESSIONS.get(token));
        }

        @Bean
        PublicRoute publicRoute() {
            return new PublicRoute();
        }

        @Bean
        AuthenticatedRoute authenticatedRoute(AtomicInteger controllerCalls) {
            return new AuthenticatedRoute(controllerCalls);
        }

        @Bean
        PermissionRoute permissionRoute() {
            return new PermissionRoute();
        }

        @Bean
        SystemRoute systemRoute() {
            return new SystemRoute();
        }

        @Bean
        AnyItemRoute anyItemRoute() {
            return new AnyItemRoute();
        }

        @Bean
        FeaturedItemRoute featuredItemRoute() {
            return new FeaturedItemRoute();
        }
    }

    @RestController
    @Access(Posture.PUBLIC)
    static class PublicRoute {

        @GetMapping("/test/public")
        String answer() {
            return "public";
        }
    }

    @RestController
    @Access(Posture.AUTHENTICATED)
    static class AuthenticatedRoute {

        private final AtomicInteger calls;

        AuthenticatedRoute(AtomicInteger calls) {
            this.calls = calls;
        }

        @GetMapping("/test/self")
        String answer(@AuthenticationPrincipal ResolvedSession caller) {
            calls.incrementAndGet();
            return caller.principalId().toString();
        }
    }

    @RestController
    @Access(value = Posture.PERMISSION, permission = "tabs.close")
    static class PermissionRoute {

        @PostMapping("/test/permission")
        String answer() {
            return "permitted";
        }
    }

    @RestController
    @Access(Posture.SYSTEM)
    static class SystemRoute {

        @PostMapping("/test/system")
        String answer() {
            return "system";
        }
    }

    @RestController
    @Access(Posture.AUTHENTICATED)
    static class AnyItemRoute {

        @GetMapping("/test/items/{id}")
        String answer() {
            return "any item";
        }
    }

    @RestController
    @Access(Posture.PUBLIC)
    static class FeaturedItemRoute {

        @GetMapping("/test/items/featured")
        String answer() {
            return "featured item";
        }
    }

    @Autowired
    MockMvcTester http;

    @Autowired
    AtomicInteger controllerCalls;

    @Autowired
    ApplicationContext context;

    @BeforeEach
    void forgetEarlierCalls() {
        controllerCalls.set(0);
    }

    @Test
    void aPublicRouteAnswersWithoutAToken() {
        assertThat(http.get().uri("/v1/test/public")).hasStatusOk().bodyText().isEqualTo("public");
    }

    @Test
    void anAuthenticatedRouteAnswersTheCallerOfAResolvedSession() {
        assertThat(http.get().uri("/v1/test/self").header(HttpHeaders.AUTHORIZATION, "Bearer person-token"))
                .hasStatusOk()
                .bodyText()
                .isEqualTo(PERSON_ID.toString());
    }

    @Test
    void theBearerSchemeIsCaseInsensitive() {
        assertThat(http.get().uri("/v1/test/self").header(HttpHeaders.AUTHORIZATION, "bearer person-token"))
                .hasStatusOk();
    }

    @Test
    void anAuthenticatedRouteRefusesARequestWithoutATokenAndAsksForABearerToken() {
        assertThat(http.get().uri("/v1/test/self"))
                .hasStatus(HttpStatus.UNAUTHORIZED)
                .hasHeader(HttpHeaders.WWW_AUTHENTICATE, "Bearer");
    }

    @Test
    void anAuthenticatedRouteRefusesAnUnresolvableToken() {
        assertThat(http.get().uri("/v1/test/self").header(HttpHeaders.AUTHORIZATION, "Bearer unknown-token"))
                .hasStatus(HttpStatus.UNAUTHORIZED);
    }

    @Test
    void aTokenInACookieIsIgnored() {
        assertThat(http.get()
                        .uri("/v1/test/self")
                        .cookie(
                                new Cookie("access_token", "person-token"),
                                new Cookie("Authorization", "person-token")))
                .hasStatus(HttpStatus.UNAUTHORIZED);
    }

    @Test
    void aTokenInAQueryParameterIsIgnored() {
        assertThat(http.get().uri("/v1/test/self?access_token=person-token")).hasStatus(HttpStatus.UNAUTHORIZED);
    }

    @Test
    void anotherAuthorizationSchemeIsIgnored() {
        assertThat(http.get().uri("/v1/test/self").header(HttpHeaders.AUTHORIZATION, "Basic person-token"))
                .hasStatus(HttpStatus.UNAUTHORIZED);
    }

    @Test
    void twoAuthorizationHeadersAreIgnored() {
        assertThat(http.get()
                        .uri("/v1/test/self")
                        .header(HttpHeaders.AUTHORIZATION, "Bearer person-token", "Bearer guest-token"))
                .hasStatus(HttpStatus.UNAUTHORIZED);
    }

    @Test
    void aRefusedRequestNeverRunsControllerCode() {
        // When
        assertThat(http.get().uri("/v1/test/self")).hasStatus(HttpStatus.UNAUTHORIZED);

        // Then
        assertThat(controllerCalls).hasValue(0);
    }

    @Test
    void aPermissionRouteIsForbiddenByTheDefaultEvaluator() {
        assertThat(http.post().uri("/v1/test/permission").header(HttpHeaders.AUTHORIZATION, "Bearer person-token"))
                .hasStatus(HttpStatus.FORBIDDEN);
    }

    @Test
    void aPermissionRouteRefusesARequestWithoutATokenAsUnauthenticated() {
        assertThat(http.post().uri("/v1/test/permission")).hasStatus(HttpStatus.UNAUTHORIZED);
    }

    @ParameterizedTest
    @EnumSource(SessionKind.class)
    void aSystemRouteIsForbiddenForEverySessionKind(SessionKind kind) {
        var token = kind.name().toLowerCase() + "-token";

        assertThat(http.post().uri("/v1/test/system").header(HttpHeaders.AUTHORIZATION, "Bearer " + token))
                .hasStatus(HttpStatus.FORBIDDEN);
    }

    @Test
    void aRequestThatMatchesNoRouteIsRefused() {
        assertThat(http.get().uri("/v1/test/unknown")).hasStatus(HttpStatus.UNAUTHORIZED);
        assertThat(http.get().uri("/v1/test/unknown").header(HttpHeaders.AUTHORIZATION, "Bearer person-token"))
                .hasStatus(HttpStatus.FORBIDDEN);
    }

    @Test
    void thePostureIsTheOneOfTheRouteSpringMvcDispatchesTo() {
        // Given /test/items/{id} (authenticated) and /test/items/featured (public) both match /test/items/featured

        // Then the more specific route, the one Spring MVC runs, decides
        assertThat(http.get().uri("/v1/test/items/featured"))
                .hasStatusOk()
                .bodyText()
                .isEqualTo("featured item");
        assertThat(http.get().uri("/v1/test/items/42")).hasStatus(HttpStatus.UNAUTHORIZED);
    }

    @Test
    void noPasswordUserIsCreated() {
        // Then Boot's generated in-memory user (and its password in the log) is off: bearer sessions are the only login
        assertThat(context.getBeanProvider(UserDetailsService.class).stream()).isEmpty();
    }

    @Test
    void theHealthEndpointStaysPublic() {
        assertThat(http.get().uri("/actuator/health")).hasStatusOk();
    }

    private static ResolvedSession session(UUID principalId, SessionKind kind) {
        return new ResolvedSession(principalId, UUID.randomUUID(), kind);
    }
}
