package com.frappe.platform.infrastructure.web;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.TestNatsConfiguration;
import com.frappe.TestcontainersConfiguration;
import com.frappe.platform.TenantScope;
import com.frappe.platform.web.Access;
import com.frappe.platform.web.BusinessMembership;
import com.frappe.platform.web.Posture;
import com.frappe.platform.web.ResolvedSession;
import com.frappe.platform.web.SessionKind;
import com.frappe.platform.web.SessionResolver;
import java.net.URI;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Optional;
import java.util.Set;
import java.util.TreeMap;
import java.util.UUID;
import java.util.concurrent.Callable;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.stream.Stream;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.Arguments;
import org.junit.jupiter.params.provider.MethodSource;
import org.junit.jupiter.params.provider.ValueSource;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Import;
import org.springframework.http.HttpHeaders;
import org.springframework.http.HttpStatus;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.test.context.ActiveProfiles;
import org.springframework.test.web.servlet.assertj.MockMvcTester;
import org.springframework.test.web.servlet.assertj.MvcTestResult;
import org.springframework.transaction.support.TransactionTemplate;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.RestController;

/**
 * Routes under {@code /v1/businesses/{businessId}/} run only for callers that may enter that business, with the
 * business bound as the tenant: row-level security on the fixture table {@code fixture.tenant_probe} proves it, run as
 * {@code frappe_app} on real Postgres. Every test creates its own businesses.
 */
@SpringBootTest
@AutoConfigureMockMvc
@Import({TestcontainersConfiguration.class, TestNatsConfiguration.class, BusinessPathTests.Routes.class})
@ActiveProfiles("local")
class BusinessPathTests {

    static final UUID MEMBER = UUID.fromString("0190a8f0-0000-7000-8000-0000000000a1");
    static final UUID STRANGER = UUID.fromString("0190a8f0-0000-7000-8000-0000000000a2");
    static final UUID BUSINESS_A = UUID.fromString("0190a8f0-0000-7000-8000-0000000000b1");
    static final UUID BUSINESS_B = UUID.fromString("0190a8f0-0000-7000-8000-0000000000b2");

    static final Map<String, ResolvedSession> SESSIONS = Map.of(
            "member-token",
            new ResolvedSession(MEMBER, UUID.randomUUID(), SessionKind.PERSON),
            "stranger-token",
            new ResolvedSession(STRANGER, UUID.randomUUID(), SessionKind.PERSON),
            "staff-of-a-token",
            bound(SessionKind.STAFF, BUSINESS_A),
            "terminal-of-a-token",
            bound(SessionKind.TERMINAL, BUSINESS_A),
            "guest-of-a-token",
            bound(SessionKind.GUEST, BUSINESS_A),
            "unbound-guest-token",
            new ResolvedSession(UUID.randomUUID(), UUID.randomUUID(), SessionKind.GUEST));

    /** A membership port for the tests: the member belongs to business A only; it can be made to fail. */
    static final class TestMembership implements BusinessMembership {

        final AtomicInteger calls = new AtomicInteger();
        final AtomicBoolean failing = new AtomicBoolean();

        @Override
        public boolean isMember(UUID personId, UUID businessId) {
            calls.incrementAndGet();
            if (failing.get()) {
                throw new IllegalStateException("membership store unavailable");
            }
            return MEMBER.equals(personId) && BUSINESS_A.equals(businessId);
        }
    }

    @TestConfiguration(proxyBeanMethods = false)
    static class Routes {

        @Bean
        AtomicInteger businessControllerCalls() {
            return new AtomicInteger();
        }

        @Bean
        SessionResolver sessionResolver() {
            return token -> Optional.ofNullable(SESSIONS.get(token));
        }

        @Bean
        TestMembership businessMembership() {
            return new TestMembership();
        }

        @Bean
        BusinessProbesRoute businessProbesRoute(
                AtomicInteger businessControllerCalls, JdbcTemplate jdbc, TransactionTemplate transactions) {
            return new BusinessProbesRoute(businessControllerCalls, jdbc, transactions);
        }

        @Bean
        PublicBusinessTenantRoute publicBusinessTenantRoute(TenantScope tenants) {
            return new PublicBusinessTenantRoute(tenants);
        }

        @Bean
        AsyncBusinessTenantRoute asyncBusinessTenantRoute(TenantScope tenants, AtomicInteger businessControllerCalls) {
            return new AsyncBusinessTenantRoute(tenants, businessControllerCalls);
        }

        @Bean
        OutsideTenantRoute outsideTenantRoute(TenantScope tenants) {
            return new OutsideTenantRoute(tenants);
        }
    }

    /** Lists every tenant probe row the request's transaction may see, unfiltered: row-level security filters. */
    @RestController
    @Access(Posture.AUTHENTICATED)
    static class BusinessProbesRoute {

        private final AtomicInteger calls;
        private final JdbcTemplate jdbc;
        private final TransactionTemplate transactions;

        BusinessProbesRoute(AtomicInteger calls, JdbcTemplate jdbc, TransactionTemplate transactions) {
            this.calls = calls;
            this.jdbc = jdbc;
            this.transactions = transactions;
        }

        @GetMapping("/businesses/{businessId}/test/probes")
        Set<UUID> probes(@PathVariable String businessId) {
            calls.incrementAndGet();
            return Set.copyOf(transactions.execute(
                    status -> jdbc.queryForList("select tenant_id from fixture.tenant_probe", UUID.class)));
        }
    }

    /** Answers the tenant bound while it runs. */
    @RestController
    @Access(Posture.PUBLIC)
    static class PublicBusinessTenantRoute {

        private final TenantScope tenants;

        PublicBusinessTenantRoute(TenantScope tenants) {
            this.tenants = tenants;
        }

        @GetMapping("/businesses/{businessId}/test/public-tenant")
        String tenant(@PathVariable String businessId) {
            return tenants.current().map(UUID::toString).orElse("none");
        }
    }

    /** Answers, from the async dispatch, the tenant bound there. */
    @RestController
    @Access(Posture.AUTHENTICATED)
    static class AsyncBusinessTenantRoute {

        private final TenantScope tenants;
        private final AtomicInteger calls;

        AsyncBusinessTenantRoute(TenantScope tenants, AtomicInteger calls) {
            this.tenants = tenants;
            this.calls = calls;
        }

        @GetMapping("/businesses/{businessId}/test/async-tenant")
        Callable<String> tenant(@PathVariable String businessId) {
            calls.incrementAndGet();
            return () -> tenants.current().map(UUID::toString).orElse("none");
        }
    }

    /** Answers the tenant bound while it runs, outside the business path. */
    @RestController
    @Access(Posture.PUBLIC)
    static class OutsideTenantRoute {

        private final TenantScope tenants;

        OutsideTenantRoute(TenantScope tenants) {
            this.tenants = tenants;
        }

        @GetMapping("/test/outside-tenant")
        String tenant() {
            return tenants.current().map(UUID::toString).orElse("none");
        }
    }

    @Autowired
    MockMvcTester http;

    @Autowired
    AtomicInteger businessControllerCalls;

    @Autowired
    TestMembership membership;

    @Autowired
    TenantScope tenants;

    @Autowired
    JdbcTemplate jdbc;

    @Autowired
    TransactionTemplate transactions;

    @BeforeEach
    void rowsOfTwoBusinesses() {
        businessControllerCalls.set(0);
        membership.calls.set(0);
        membership.failing.set(false);
        insertProbe(BUSINESS_A);
        insertProbe(BUSINESS_B);
    }

    @Test
    void aMemberSeesOnlyTheRowsOfTheBusinessInThePath() {
        assertThat(get("/v1/businesses/" + BUSINESS_A + "/test/probes", "member-token"))
                .hasStatusOk()
                .bodyJson()
                .isEqualTo("[\"" + BUSINESS_A + "\"]");
    }

    @Test
    void aNonMemberIsNotFoundLikeAnUnknownBusinessAndNoControllerCodeRuns() {
        // When
        var nonMember = get("/v1/businesses/" + BUSINESS_A + "/test/probes", "stranger-token");
        var unknownBusiness = get("/v1/businesses/" + UUID.randomUUID() + "/test/probes", "member-token");

        // Then
        assertThat(nonMember)
                .hasStatus(HttpStatus.NOT_FOUND)
                .bodyJson()
                .extractingPath("$.code")
                .isEqualTo("not-found");
        assertThat(unknownBusiness)
                .hasStatus(HttpStatus.NOT_FOUND)
                .bodyJson()
                .extractingPath("$.code")
                .isEqualTo("not-found");
        assertThat(businessControllerCalls).hasValue(0);
    }

    @Test
    void aStaffSessionNeverCrossesIntoAnotherBusiness() {
        assertThat(get("/v1/businesses/" + BUSINESS_B + "/test/probes", "staff-of-a-token"))
                .hasStatus(HttpStatus.NOT_FOUND);
        assertThat(get("/v1/businesses/" + BUSINESS_B + "/test/probes", "terminal-of-a-token"))
                .hasStatus(HttpStatus.NOT_FOUND);
        assertThat(businessControllerCalls).hasValue(0);
    }

    @Test
    void aStaffSessionEntersItsOwnBusinessWithoutAskingForMembership() {
        assertThat(get("/v1/businesses/" + BUSINESS_A + "/test/probes", "staff-of-a-token"))
                .hasStatusOk()
                .bodyJson()
                .isEqualTo("[\"" + BUSINESS_A + "\"]");
        assertThat(membership.calls).hasValue(0);
    }

    @Test
    void aSessionOfAnotherKindWithoutABindingEntersNoBusiness() {
        assertThat(get("/v1/businesses/" + BUSINESS_A + "/test/probes", "unbound-guest-token"))
                .hasStatus(HttpStatus.NOT_FOUND);
        assertThat(membership.calls).hasValue(0);
    }

    @Test
    void aPublicRouteUnderThePathRunsAnonymouslyWithTheBusinessBound() {
        assertThat(http.get().uri("/v1/businesses/" + BUSINESS_B + "/test/public-tenant"))
                .hasStatusOk()
                .bodyText()
                .isEqualTo(BUSINESS_B.toString());
        assertThat(membership.calls).hasValue(0);
    }

    @ParameterizedTest
    @ValueSource(
            strings = {
                "/v1/businesses/not-a-uuid/test/probes",
                "/v1/businesses/1-1-1-1-1/test/probes",
                "/v1/businesses/not-a-uuid/test/public-tenant"
            })
    void aMalformedBusinessIdIsNotFound(String path) {
        assertThat(get(path, "member-token"))
                .hasStatus(HttpStatus.NOT_FOUND)
                .bodyJson()
                .extractingPath("$.code")
                .isEqualTo("not-found");
        assertThat(businessControllerCalls).hasValue(0);
    }

    @Test
    void aPathOutsideTheBusinessesBindsNoTenant() {
        assertThat(http.get().uri("/v1/test/outside-tenant"))
                .hasStatusOk()
                .bodyText()
                .isEqualTo("none");
    }

    @Test
    void membershipIsAskedOnEveryRequestAndNeverCached() {
        // When
        get("/v1/businesses/" + BUSINESS_A + "/test/probes", "member-token");
        get("/v1/businesses/" + BUSINESS_A + "/test/probes", "member-token");

        // Then
        assertThat(membership.calls).hasValue(2);
    }

    @Test
    void aFailingMembershipPortAnswersTheGenericInternalError() {
        // Given
        membership.failing.set(true);

        // When
        var result = get("/v1/businesses/" + BUSINESS_A + "/test/probes", "member-token");

        // Then
        assertThat(result)
                .hasStatus(HttpStatus.INTERNAL_SERVER_ERROR)
                .bodyJson()
                .extractingPath("$.code")
                .isEqualTo("internal-error");
        assertThat(businessControllerCalls).hasValue(0);
    }

    @Test
    void aGuestSessionNeverCrossesIntoAnotherBusiness() {
        assertThat(get("/v1/businesses/" + BUSINESS_B + "/test/probes", "guest-of-a-token"))
                .hasStatus(HttpStatus.NOT_FOUND);
        assertThat(businessControllerCalls).hasValue(0);
    }

    static Stream<Arguments> pathTricks() {
        var a = BUSINESS_A.toString();
        var b = BUSINESS_B.toString();
        var tricks = new ArrayList<Arguments>();
        // The stranger is a member of nothing; staff of A tries B.
        for (var target : List.of(Map.entry("stranger-token", a), Map.entry("staff-of-a-token", b))) {
            var token = target.getKey();
            var id = target.getValue();
            tricks.add(Arguments.of(token, "/v1/businesses/" + id + "/test/probes/"));
            tricks.add(Arguments.of(token, "/v1/businesses//" + id + "/test/probes"));
            tricks.add(Arguments.of(token, "/v1/businesses/" + id + "//test/probes"));
            tricks.add(Arguments.of(token, "/v1/businesses/" + id + ";matrix=1/test/probes"));
            tricks.add(Arguments.of(token, "/v1/businesses;x=1/" + id + "/test/probes"));
            tricks.add(Arguments.of(token, "/v1/businesses/" + id + "%2Ftest/probes"));
            tricks.add(Arguments.of(token, "/v1/businesses%2F" + id + "/test/probes"));
            tricks.add(Arguments.of(token, "/v1/businesses/" + id.toUpperCase(Locale.ROOT) + "/test/probes"));
            tricks.add(Arguments.of(
                    token, "/v1/businesses/%" + Integer.toHexString(id.charAt(0)) + id.substring(1) + "/test/probes"));
        }
        return tricks.stream();
    }

    @ParameterizedTest(name = "{0} {1}")
    @MethodSource("pathTricks")
    void noPathTrickLetsACallerIntoABusinessItMayNotEnter(String token, String path) {
        // When
        var result = http.get()
                .uri(URI.create(path))
                .header(HttpHeaders.AUTHORIZATION, "Bearer " + token)
                .exchange();

        // Then
        assertThat(result.getResponse().getStatus()).isIn(400, 404);
        assertThat(businessControllerCalls).hasValue(0);
    }

    @Test
    void anAnonymousCallerWithAMalformedIdOnAPublicRouteIsNotFound() {
        assertThat(http.get().uri("/v1/businesses/not-a-uuid/test/public-tenant"))
                .hasStatus(HttpStatus.NOT_FOUND)
                .bodyJson()
                .extractingPath("$.code")
                .isEqualTo("not-found");
    }

    @Test
    void aPublicRouteRunsForAnUnknownBusinessWithItBound() {
        var unknown = UUID.randomUUID();

        assertThat(http.get().uri("/v1/businesses/" + unknown + "/test/public-tenant"))
                .hasStatusOk()
                .bodyText()
                .isEqualTo(unknown.toString());
    }

    @Test
    void theRefusalOfANonMemberIsIndistinguishableFromAnUnknownBusiness() throws Exception {
        // When
        var nonMember = get("/v1/businesses/" + BUSINESS_A + "/test/probes", "stranger-token");
        var unknown = get("/v1/businesses/" + UUID.randomUUID() + "/test/probes", "stranger-token");

        // Then: identical status, headers and body, but for the path the client requested and the request's trace id
        assertThat(nonMember.getResponse().getStatus())
                .isEqualTo(unknown.getResponse().getStatus());
        assertThat(headersOf(nonMember)).isEqualTo(headersOf(unknown));
        assertThat(normalizedBody(nonMember)).isEqualTo(normalizedBody(unknown));
    }

    @Test
    void anAsyncRouteRunsItsWorkWithoutTheTenantSoItSeesNoRows() {
        // Given the check already ran on the request dispatch: a stranger never reaches the route
        assertThat(get("/v1/businesses/" + BUSINESS_A + "/test/async-tenant", "stranger-token"))
                .hasStatus(HttpStatus.NOT_FOUND);
        assertThat(businessControllerCalls).hasValue(0);

        // When a member's async work runs on another thread, then no tenant is bound there (fail closed)
        assertThat(get("/v1/businesses/" + BUSINESS_A + "/test/async-tenant", "member-token"))
                .hasStatusOk()
                .bodyText()
                .isEqualTo("none");
        assertThat(membership.calls).hasValue(2);
    }

    @Test
    void theTenantIsUnboundAfterABusinessRequest() {
        // Given
        assertThat(get("/v1/businesses/" + BUSINESS_A + "/test/probes", "member-token"))
                .hasStatusOk();

        // Then the same thread carries nothing to its next request
        assertThat(tenants.current()).isEmpty();
        assertThat(http.get().uri("/v1/test/outside-tenant"))
                .hasStatusOk()
                .bodyText()
                .isEqualTo("none");
    }

    private static Map<String, List<String>> headersOf(MvcTestResult result) {
        var response = result.getResponse();
        var headers = new TreeMap<String, List<String>>();
        for (var name : response.getHeaderNames()) {
            if (!name.equalsIgnoreCase(HttpHeaders.CONTENT_LENGTH)) {
                headers.put(name, response.getHeaders(name));
            }
        }
        return headers;
    }

    private static String normalizedBody(MvcTestResult result) throws Exception {
        return result.getResponse()
                .getContentAsString()
                .replaceAll("\"instance\":\"[^\"]*\"", "\"instance\":\"<path>\"")
                // Every request has its own trace id; its shape is the same for both.
                .replaceAll("\"traceId\":\"[0-9a-f]{32}\"", "\"traceId\":\"<trace>\"");
    }

    private MvcTestResult get(String path, String token) {
        return http.get()
                .uri(path)
                .header(HttpHeaders.AUTHORIZATION, "Bearer " + token)
                .exchange();
    }

    private void insertProbe(UUID businessId) {
        tenants.runAs(
                businessId,
                () -> transactions.executeWithoutResult(status -> jdbc.update(
                        "insert into fixture.tenant_probe (id, tenant_id, label) values (?, ?, 'probe')",
                        UUID.randomUUID(),
                        businessId)));
    }

    private static ResolvedSession bound(SessionKind kind, UUID businessId) {
        return new ResolvedSession(
                UUID.randomUUID(), UUID.randomUUID(), kind, Optional.of(businessId), Optional.empty());
    }
}
