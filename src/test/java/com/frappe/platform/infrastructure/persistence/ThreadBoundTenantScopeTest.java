package com.frappe.platform.infrastructure.persistence;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;
import org.springframework.transaction.support.TransactionSynchronizationManager;

class ThreadBoundTenantScopeTest {

    static final UUID TENANT_A = UUID.fromString("0199a000-0000-7000-8000-00000000000a");
    static final UUID TENANT_B = UUID.fromString("0199a000-0000-7000-8000-00000000000b");

    final ThreadBoundTenantScope scope = new ThreadBoundTenantScope();

    @AfterEach
    void endTransaction() {
        TransactionSynchronizationManager.setActualTransactionActive(false);
    }

    @Test
    void noTenantIsBoundOutsideAScope() {
        assertThat(scope.current()).isEmpty();
    }

    @Test
    void theTenantIsBoundWhileTheWorkRunsAndUnboundAfterwards() {
        // When
        var seen = scope.callAs(TENANT_A, scope::current);

        // Then
        assertThat(seen).contains(TENANT_A);
        assertThat(scope.current()).isEmpty();
    }

    @Test
    void theTenantIsUnboundWhenTheWorkThrows() {
        // When
        assertThatThrownBy(() -> scope.runAs(TENANT_A, () -> {
                    throw new IllegalArgumentException("work failed");
                }))
                .isInstanceOf(IllegalArgumentException.class);

        // Then
        assertThat(scope.current()).isEmpty();
    }

    @Test
    void bindingAnotherTenantInsideABoundScopeIsRejected() {
        assertThatThrownBy(() -> scope.runAs(TENANT_A, () -> scope.runAs(TENANT_B, () -> {})))
                .isInstanceOf(IllegalStateException.class);
    }

    @Test
    void bindingTheBoundTenantAgainKeepsItBound() {
        // When
        var seen = scope.callAs(TENANT_A, () -> scope.callAs(TENANT_A, scope::current));

        // Then
        assertThat(seen).contains(TENANT_A);
    }

    @Test
    void bindingInsideARunningTransactionIsRejected() {
        // Given
        TransactionSynchronizationManager.setActualTransactionActive(true);

        // Then
        assertThatThrownBy(() -> scope.runAs(TENANT_A, () -> {})).isInstanceOf(IllegalStateException.class);
    }

    @Test
    void theBindingBelongsToItsThread() {
        // When
        var otherThread = scope.callAs(
                TENANT_A, () -> CompletableFuture.supplyAsync(scope::current).join());

        // Then
        assertThat(otherThread).isEmpty();
    }
}
