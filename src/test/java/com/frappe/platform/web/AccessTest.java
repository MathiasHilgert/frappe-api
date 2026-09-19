package com.frappe.platform.web;

import static org.assertj.core.api.Assertions.assertThat;

import java.lang.reflect.Method;
import java.util.Arrays;
import org.junit.jupiter.api.Test;

/** The HTTP layer authenticates only; authorization belongs to the application layer. */
class AccessTest {

    @Test
    void aPostureOnlyStatesWhetherTheCallerMustBeAuthenticated() {
        assertThat(Posture.values()).containsExactly(Posture.PUBLIC, Posture.AUTHENTICATED);
    }

    @Test
    void accessDeclaresAPostureAndNoPermission() {
        assertThat(Arrays.stream(Access.class.getDeclaredMethods()).map(Method::getName))
                .containsExactly("value");
    }
}
