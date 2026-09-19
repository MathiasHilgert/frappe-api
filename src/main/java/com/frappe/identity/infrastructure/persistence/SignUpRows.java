package com.frappe.identity.infrastructure.persistence;

import java.util.UUID;
import org.springframework.data.jpa.repository.JpaRepository;

/** Spring Data reads of {@code identity.sign_up}; starts are written by {@link JpaSignUps}'s upsert. */
interface SignUpRows extends JpaRepository<SignUpEntity, UUID> {}
