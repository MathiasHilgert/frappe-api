package com.frappe.platform.infrastructure.web;

import com.frappe.platform.web.SessionResolver;
import jakarta.servlet.DispatcherType;
import java.util.Optional;
import org.springframework.beans.factory.ObjectProvider;
import org.springframework.boot.health.actuate.endpoint.HealthEndpoint;
import org.springframework.boot.security.autoconfigure.actuate.web.servlet.EndpointRequest;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.security.config.annotation.web.builders.HttpSecurity;
import org.springframework.security.config.annotation.web.configurers.AbstractHttpConfigurer;
import org.springframework.security.config.http.SessionCreationPolicy;
import org.springframework.security.web.SecurityFilterChain;
import org.springframework.security.web.authentication.AnonymousAuthenticationFilter;

/**
 * The one security filter chain: stateless, bearer tokens only, every request decided by its route's posture before
 * controller code runs. It authenticates only; authorization (roles per branch) is the application layer's.
 */
@Configuration(proxyBeanMethods = false)
class SecurityConfiguration {

    /** springdoc's spec and swagger-ui paths; swagger-ui itself is only served in the local profile. */
    private static final String[] API_DOCUMENTATION = {
        "/v3/api-docs", "/v3/api-docs/**", "/swagger-ui.html", "/swagger-ui/**"
    };

    /** Until identity provides sessions, no token resolves. */
    private static final SessionResolver NO_SESSIONS = token -> Optional.empty();

    /** Creates the configuration; instantiated by Spring. */
    SecurityConfiguration() {}

    /**
     * The API's security filter chain. No session, cookie, CSRF token, login form, basic authentication or saved
     * request: the {@code Authorization: Bearer} header is the only credential. Error dispatches (rendering a refusal
     * already decided), the health endpoint (probes) and the API documentation are public; everything else is decided
     * by its route.
     *
     * @param http Spring Security's builder
     * @param routes the checked routes
     * @param sessionResolver identity's session resolver, when present
     * @return the filter chain
     * @throws Exception when Spring Security cannot build the chain
     */
    @Bean
    SecurityFilterChain apiSecurityFilterChain(
            HttpSecurity http, RouteCatalog routes, ObjectProvider<SessionResolver> sessionResolver) throws Exception {
        var bearerSessions = new BearerSessionFilter(sessionResolver.getIfAvailable(() -> NO_SESSIONS));
        var routeAuthorization = new RouteAuthorizationManager(routes);
        return http.csrf(AbstractHttpConfigurer::disable)
                .sessionManagement(sessions -> sessions.sessionCreationPolicy(SessionCreationPolicy.STATELESS))
                .requestCache(AbstractHttpConfigurer::disable)
                .formLogin(AbstractHttpConfigurer::disable)
                .httpBasic(AbstractHttpConfigurer::disable)
                .logout(AbstractHttpConfigurer::disable)
                .exceptionHandling(failures -> failures.authenticationEntryPoint(new BearerAuthenticationEntryPoint()))
                .addFilterBefore(bearerSessions, AnonymousAuthenticationFilter.class)
                .authorizeHttpRequests(requests -> requests.dispatcherTypeMatchers(DispatcherType.ERROR)
                        .permitAll()
                        .requestMatchers(EndpointRequest.to(HealthEndpoint.class))
                        .permitAll()
                        .requestMatchers(API_DOCUMENTATION)
                        .permitAll()
                        .anyRequest()
                        .access(routeAuthorization))
                .build();
    }
}
