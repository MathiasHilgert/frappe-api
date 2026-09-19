package com.frappe.platform.infrastructure.digests;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatIllegalArgumentException;
import static org.assertj.core.api.Assertions.assertThatNullPointerException;
import static org.assertj.core.api.Assertions.catchThrowable;

import com.frappe.platform.IdentityLimits;
import com.frappe.platform.IdentitySecrets;
import com.frappe.platform.LimitKey;
import com.frappe.platform.SecretKey;
import java.nio.charset.StandardCharsets;
import java.util.Base64;
import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;
import org.springframework.boot.test.system.CapturedOutput;
import org.springframework.boot.test.system.OutputCaptureExtension;

class HmacKeyedDigestsTest {

    private static final String PEPPER = "not-a-secret-".repeat(3);

    private static final String EMAIL = "ana@example.com";

    private final HmacKeyedDigests digests = new HmacKeyedDigests(PEPPER);

    @Test
    void theSameValueGivesTheSameSubjectAndDigestOnEveryInstanceSharingTheKey() {
        // Given a second instance with the same key
        var other = new HmacKeyedDigests(PEPPER);

        // Then
        assertThat(digests.subjectOf("identity.email", EMAIL)).isEqualTo(other.subjectOf("identity.email", EMAIL));
        assertThat(digests.subjectOf("identity.email", EMAIL)).isEqualTo(digests.subjectOf("identity.email", EMAIL));
        assertThat(digests.digestOf("identity.email", EMAIL)).isEqualTo(other.digestOf("identity.email", EMAIL));
    }

    @Test
    void theDigestIsTheBase64UrlHmacOfTheNamespaceASeparatorAndTheValue() throws Exception {
        // Given the MAC computed independently
        var mac = Mac.getInstance("HmacSHA256");
        mac.init(new SecretKeySpec(PEPPER.getBytes(StandardCharsets.UTF_8), "HmacSHA256"));
        var expected = Base64.getUrlEncoder()
                .withoutPadding()
                .encodeToString(mac.doFinal("identity.email\0ana@example.com".getBytes(StandardCharsets.UTF_8)));

        // Then
        assertThat(digests.digestOf("identity.email", EMAIL))
                .isEqualTo(expected)
                .hasSize(43);
    }

    @Test
    void oneValueGetsDifferentSubjectsAndDigestsInTwoNamespaces() {
        assertThat(digests.subjectOf("identity.email", EMAIL))
                .isNotEqualTo(digests.subjectOf("identity.recovery-code", EMAIL));
        assertThat(digests.digestOf("identity.email", EMAIL))
                .isNotEqualTo(digests.digestOf("identity.recovery-code", EMAIL));
    }

    @Test
    void anotherKeyGivesAnotherSubject() {
        assertThat(new HmacKeyedDigests("another-non-secret-".repeat(2)).subjectOf("identity.email", EMAIL))
                .isNotEqualTo(digests.subjectOf("identity.email", EMAIL));
    }

    @Test
    void subjectsAreVersion8UuidsThatLimitAndSecretKeysAccept() {
        // When
        var subject = digests.subjectOf("identity.email", EMAIL);

        // Then
        assertThat(subject.version()).isEqualTo(8);
        assertThat(subject.variant()).isEqualTo(2);
        assertThat(subject.toString()).isLowerCase();
        assertThat(LimitKey.ofId(IdentityLimits.LOGIN_PER_ACCOUNT, subject).subject())
                .isEqualTo(subject.toString());
        assertThat(SecretKey.of(IdentitySecrets.EMAIL_PROOF, subject).subjectId())
                .isEqualTo(subject);
    }

    @ParameterizedTest
    @ValueSource(strings = {"Identity.email", "identity.Email", "", " ", "email", ".email", "identity.", "a.b.c"})
    void rejectsANamespaceThatIsNotModuleDotKebabName(String namespace) {
        assertThatIllegalArgumentException().isThrownBy(() -> digests.subjectOf(namespace, EMAIL));
        assertThatIllegalArgumentException().isThrownBy(() -> digests.digestOf(namespace, EMAIL));
    }

    @Test
    void rejectsNull() {
        assertThatNullPointerException().isThrownBy(() -> digests.subjectOf(null, EMAIL));
        assertThatNullPointerException().isThrownBy(() -> digests.digestOf("identity.email", null));
    }

    @Test
    void rejectsAKeyShorterThan32CharactersWithoutEchoingIt() {
        assertThatIllegalArgumentException()
                .isThrownBy(() -> new HmacKeyedDigests("too-short-digest-key"))
                .withMessageContaining("FRAPPE_DIGEST_PEPPER")
                .withMessageNotContaining("too-short-digest-key");
    }

    @Test
    @ExtendWith(OutputCaptureExtension.class)
    void neitherTheValueNorTheKeyAppearsInLogsOrErrors(CapturedOutput output) {
        // When digests are computed and fail
        digests.subjectOf("identity.email", EMAIL);
        digests.digestOf("identity.email", EMAIL);
        var failure = catchThrowable(() -> digests.digestOf("Identity", EMAIL));

        // Then
        assertThat(failure).hasMessageNotContaining(EMAIL).hasMessageNotContaining(PEPPER);
        assertThat(output.getAll()).doesNotContain(EMAIL).doesNotContain(PEPPER);
        assertThat(digests.toString()).doesNotContain(PEPPER);
    }
}
