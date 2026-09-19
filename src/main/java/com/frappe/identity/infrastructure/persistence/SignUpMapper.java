package com.frappe.identity.infrastructure.persistence;

import com.frappe.identity.domain.EmailAddress;
import com.frappe.identity.domain.SignUp;
import com.frappe.identity.domain.SignUpId;
import java.util.Locale;
import org.mapstruct.Mapper;

/**
 * Maps stored sign-ups to the aggregate. {@link SignUp} has no public constructor or setters, so the mapping is the one
 * call to {@link SignUp#reconstitute}; MapStruct generates the Spring bean around it. Only reads map: writes are one
 * upsert statement ({@link JpaSignUps}), because a load-then-save cannot record concurrent starts for one address.
 */
@Mapper(componentModel = "spring")
interface SignUpMapper {

    /**
     * The aggregate of a row.
     *
     * @param row the row
     * @return the sign-up
     */
    default SignUp toDomain(SignUpEntity row) {
        return SignUp.reconstitute(
                new SignUpId(row.id()),
                new EmailAddress(row.email()),
                row.emailSubject(),
                Locale.forLanguageTag(row.locale()),
                row.startedAt(),
                row.version());
    }
}
