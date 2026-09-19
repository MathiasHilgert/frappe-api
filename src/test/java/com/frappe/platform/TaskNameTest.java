package com.frappe.platform;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatIllegalArgumentException;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;

class TaskNameTest {

    @Test
    void isModuleDotKebabCase() {
        assertThat(TaskName.of("organization.close-business-day").value()).isEqualTo("organization.close-business-day");
    }

    @ParameterizedTest
    @ValueSource(
            strings = {"outbox-recovery", "Platform.outbox", "platform.outbox_recovery", "platform.", "platform.a.b"})
    void rejectsANameThatIsNotModuleDotKebabCase(String name) {
        assertThatIllegalArgumentException()
                .isThrownBy(() -> TaskName.of(name))
                .withMessageContaining(name)
                .withMessageContaining("<module>.<kebab-case-name>");
    }

    @Test
    void readsAsItsValue() {
        assertThat(TaskName.of("platform.outbox-recovery")).hasToString("platform.outbox-recovery");
    }
}
