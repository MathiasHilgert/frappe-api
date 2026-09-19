package com.frappe.platform.infrastructure.web;

import static org.assertj.core.api.Assertions.assertThat;

import com.frappe.platform.web.ResolvedSession;
import com.frappe.platform.web.SessionKind;
import java.util.Optional;
import java.util.UUID;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;
import org.springframework.http.HttpHeaders;
import org.springframework.mock.web.MockFilterChain;
import org.springframework.mock.web.MockHttpServletRequest;
import org.springframework.mock.web.MockHttpServletResponse;
import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.security.web.context.RequestAttributeSecurityContextRepository;

/** The filter keeps the caller for every later dispatch of the request, not only for the current thread. */
class BearerSessionFilterTest {

    private final ResolvedSession session =
            new ResolvedSession(UUID.randomUUID(), UUID.randomUUID(), SessionKind.PERSON);
    private final RequestAttributeSecurityContextRepository repository =
            new RequestAttributeSecurityContextRepository();

    @AfterEach
    void clearTheThread() {
        SecurityContextHolder.clearContext();
    }

    @Test
    void savesTheCallerInTheRequestForAsyncAndErrorDispatches() throws Exception {
        // Given
        var filter = new BearerSessionFilter(token -> Optional.of(session), repository);
        var request = new MockHttpServletRequest("GET", "/v1/test/self");
        request.addHeader(HttpHeaders.AUTHORIZATION, "Bearer person-token");

        // When
        filter.doFilter(request, new MockHttpServletResponse(), new MockFilterChain());

        // Then
        assertThat(repository.containsContext(request)).isTrue();
        assertThat(repository
                        .loadDeferredContext(request)
                        .get()
                        .getAuthentication()
                        .getPrincipal())
                .isEqualTo(session);
    }

    @Test
    void savesNothingForAnUnresolvedToken() throws Exception {
        // Given
        var filter = new BearerSessionFilter(token -> Optional.empty(), repository);
        var request = new MockHttpServletRequest("GET", "/v1/test/self");
        request.addHeader(HttpHeaders.AUTHORIZATION, "Bearer unknown-token");

        // When
        filter.doFilter(request, new MockHttpServletResponse(), new MockFilterChain());

        // Then
        assertThat(repository.containsContext(request)).isFalse();
    }
}
