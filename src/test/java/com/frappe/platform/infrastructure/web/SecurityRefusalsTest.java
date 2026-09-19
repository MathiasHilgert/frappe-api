package com.frappe.platform.infrastructure.web;

import static org.assertj.core.api.Assertions.assertThat;

import org.junit.jupiter.api.Test;
import org.springframework.mock.web.MockHttpServletRequest;
import org.springframework.mock.web.MockHttpServletResponse;
import org.springframework.security.access.AccessDeniedException;
import org.springframework.security.authentication.InsufficientAuthenticationException;
import org.springframework.web.servlet.ModelAndView;

class SecurityRefusalsTest {

    @Test
    void aRefusalNoResolverAnswersStillGetsItsStatus() throws Exception {
        // Given resolvers that answer nothing
        var refusals = new SecurityRefusals((request, response, handler, ex) -> null);
        var unauthenticated = new MockHttpServletResponse();
        var forbidden = new MockHttpServletResponse();

        // When
        refusals.commence(
                new MockHttpServletRequest(), unauthenticated, new InsufficientAuthenticationException("anonymous"));
        refusals.handle(new MockHttpServletRequest(), forbidden, new AccessDeniedException("denied"));

        // Then
        assertThat(unauthenticated.getStatus()).isEqualTo(401);
        assertThat(forbidden.getStatus()).isEqualTo(403);
    }

    @Test
    void aResolvedRefusalIsLeftToTheResolver() throws Exception {
        // Given a resolver that wrote the answer
        var refusals = new SecurityRefusals((request, response, handler, ex) -> new ModelAndView());
        var response = new MockHttpServletResponse();

        // When
        refusals.commence(new MockHttpServletRequest(), response, new InsufficientAuthenticationException("anonymous"));

        // Then
        assertThat(response.isCommitted()).isFalse();
        assertThat(response.getStatus()).isEqualTo(200);
    }
}
