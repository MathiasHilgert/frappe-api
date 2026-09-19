package com.frappe.platform.infrastructure.web;

import com.frappe.platform.web.SessionResolver;
import io.micrometer.observation.ObservationPredicate;
import jakarta.servlet.DispatcherType;
import java.util.List;
import java.util.Optional;
import org.springframework.beans.factory.ObjectProvider;
import org.springframework.beans.factory.annotation.Qualifier;
import org.springframework.boot.health.actuate.endpoint.HealthEndpoint;
import org.springframework.boot.security.autoconfigure.actuate.web.servlet.EndpointRequest;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.security.config.annotation.web.builders.HttpSecurity;
import org.springframework.security.config.annotation.web.configurers.AbstractHttpConfigurer;
import org.springframework.security.config.http.SessionCreationPolicy;
import org.springframework.security.web.SecurityFilterChain;
import org.springframework.security.web.authentication.AnonymousAuthenticationFilter;
import org.springframework.security.web.context.RequestAttributeSecurityContextRepository;
import org.springframework.web.servlet.HandlerExceptionResolver;

/**
 * The one security filter chain: stateless, bearer tokens only, every request decided by its route's posture before
 * controller code runs. It authenticates only; authorization (roles per branch) is the application layer's.
 */
@Configuration(proxyBeanMethods = false)
class SecurityConfiguration {

    /** springdoc's spec (JSON and YAML) and the Scalar API reference; Scalar is only served in the local profile. */
    private static final List<String> API_DOCUMENTATION =
            List.of("/v3/api-docs", "/v3/api-docs.yaml", "/v3/api-docs/**", "/scalar", "/scalar/**");

    /** Until identity provides sessions, no token resolves. */
    private static final SessionResolver NO_SESSIONS = token -> Optional.empty();

    /** Creates the configuration; instantiated by Spring. */
    SecurityConfiguration() {}

    /**
     * Keeps Spring Security's per-request filter-chain observations out of traces and metrics.
     *
     * @return the observation predicate, applied by Spring Boot to the observation registry
     */
    @Bean
    ObservationPredicate securityFilterChainObservations() {
        return new SecurityFilterChainObservations();
    }

    /**
     * The API's security filter chain. No session, cookie, CSRF token, login form, basic authentication or saved
     * request: the {@code Authorization: Bearer} header is the only credential. Error dispatches (rendering a refusal
     * already decided), the health endpoint (probes) and the API documentation are public, other actuator endpoints are
     * closed; everything else is decided by the route that serves it.
     *
     * @param http Spring Security's builder
     * @param routes the checked routes
     * @param otherHandlers the guard over handler mappings outside the routes
     * @param sessionResolver identity's session resolver, when present
     * @param exceptionResolver Spring MVC's exception resolvers, which answer the chain's refusals as problems
     * @return the filter chain
     * @throws Exception when Spring Security cannot build the chain
     */
    @Bean
    SecurityFilterChain apiSecurityFilterChain(
            HttpSecurity http,
            RouteCatalog routes,
            HandlerMappingGuard otherHandlers,
            ObjectProvider<SessionResolver> sessionResolver,
            @Qualifier("handlerExceptionResolver") HandlerExceptionResolver exceptionResolver)
            throws Exception {
        // Stateless: the context lives in a request attribute, saved once and reloaded on async and error dispatches.
        var contexts = new RequestAttributeSecurityContextRepository();
        var bearerSessions = new BearerSessionFilter(sessionResolver.getIfAvailable(() -> NO_SESSIONS), contexts);
        var routeAuthorization = new RouteAuthorizationManager(routes, otherHandlers);
        var refusals = new SecurityRefusals(exceptionResolver);
        // CSRF protection is off on purpose: this API is stateless and takes credentials only from the
        // Authorization: Bearer header, which browsers never attach on their own; cookies and query tokens are ignored
        // (pinned by RouteAccessTests). With no ambient credential there is nothing to forge, so CSRF does not apply
        // (OWASP CSRF Prevention Cheat Sheet; Spring Security reference, "When to use CSRF protection").
        return http.csrf(AbstractHttpConfigurer::disable)
                .sessionManagement(sessions -> sessions.sessionCreationPolicy(SessionCreationPolicy.STATELESS))
                .securityContext(context -> context.securityContextRepository(contexts))
                .requestCache(AbstractHttpConfigurer::disable)
                .formLogin(AbstractHttpConfigurer::disable)
                .httpBasic(AbstractHttpConfigurer::disable)
                .logout(AbstractHttpConfigurer::disable)
                .exceptionHandling(
                        failures -> failures.authenticationEntryPoint(refusals).accessDeniedHandler(refusals))
                .addFilterBefore(bearerSessions, AnonymousAuthenticationFilter.class)
                .authorizeHttpRequests(requests -> requests.dispatcherTypeMatchers(DispatcherType.ERROR)
                        .permitAll()
                        .requestMatchers(EndpointRequest.to(HealthEndpoint.class))
                        .permitAll()
                        .requestMatchers(EndpointRequest.toAnyEndpoint())
                        .denyAll()
                        .requestMatchers(API_DOCUMENTATION.toArray(String[]::new))
                        .permitAll()
                        .anyRequest()
                        .access(routeAuthorization))
                .build();
    }
}
