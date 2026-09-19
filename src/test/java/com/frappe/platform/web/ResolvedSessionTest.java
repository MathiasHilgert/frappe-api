package com.frappe.platform.web;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import java.util.Optional;
import java.util.UUID;
import org.junit.jupiter.api.Test;

/** A session is bound to at most one business, and a person's session to none. */
class ResolvedSessionTest {

    private static final UUID BUSINESS = UUID.fromString("0190a8f0-0000-7000-8000-00000000000b");
    private static final UUID BRANCH = UUID.fromString("0190a8f0-0000-7000-8000-00000000000c");

    @Test
    void aSessionWithoutABindingBelongsToNoBusinessOrBranch() {
        var session = new ResolvedSession(UUID.randomUUID(), UUID.randomUUID(), SessionKind.PERSON);

        assertThat(session.businessId()).isEmpty();
        assertThat(session.branchId()).isEmpty();
    }

    @Test
    void aStaffSessionCarriesItsBusinessAndBranch() {
        var session = new ResolvedSession(
                UUID.randomUUID(), UUID.randomUUID(), SessionKind.STAFF, Optional.of(BUSINESS), Optional.of(BRANCH));

        assertThat(session.businessId()).contains(BUSINESS);
        assertThat(session.branchId()).contains(BRANCH);
    }

    @Test
    void aPersonSessionIsNeverBoundToABusiness() {
        assertThatThrownBy(() -> new ResolvedSession(
                        UUID.randomUUID(),
                        UUID.randomUUID(),
                        SessionKind.PERSON,
                        Optional.of(BUSINESS),
                        Optional.empty()))
                .isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("PERSON");
    }

    @Test
    void aBranchIsNeverBoundWithoutItsBusiness() {
        assertThatThrownBy(() -> new ResolvedSession(
                        UUID.randomUUID(),
                        UUID.randomUUID(),
                        SessionKind.TERMINAL,
                        Optional.empty(),
                        Optional.of(BRANCH)))
                .isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("branch");
    }
}
