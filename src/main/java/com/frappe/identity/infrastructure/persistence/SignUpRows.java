package com.frappe.identity.infrastructure.persistence;

import java.util.Optional;
import java.util.UUID;
import org.springframework.data.jpa.repository.JpaRepository;

/** Spring Data access to {@code identity.sign_up}. */
interface SignUpRows extends JpaRepository<SignUpEntity, UUID> {

    /**
     * Finds the row of an address.
     *
     * @param emailSubject the keyed digest of the canonical address
     * @return the row, if any
     */
    Optional<SignUpEntity> findByEmailSubject(UUID emailSubject);
}
