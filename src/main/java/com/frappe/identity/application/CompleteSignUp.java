package com.frappe.identity.application;

import com.frappe.identity.IdentityLimits;
import com.frappe.identity.IdentitySecrets;
import com.frappe.identity.PersonRegistered;
import com.frappe.identity.domain.EmailAddress;
import com.frappe.identity.domain.LegalAcceptance;
import com.frappe.identity.domain.LegalVersions;
import com.frappe.identity.domain.NameRejected;
import com.frappe.identity.domain.Password;
import com.frappe.identity.domain.PasswordHasher;
import com.frappe.identity.domain.PasswordPolicy;
import com.frappe.identity.domain.Person;
import com.frappe.identity.domain.PersonId;
import com.frappe.identity.domain.PersonName;
import com.frappe.identity.domain.Persons;
import com.frappe.identity.domain.RecoveryCode;
import com.frappe.identity.domain.RecoveryCodes;
import com.frappe.identity.domain.RecoveryCodesIssued;
import com.frappe.identity.domain.Secrets;
import com.frappe.platform.CommandUseCase;
import com.frappe.platform.DomainEventPublisher;
import com.frappe.platform.IdGenerator;
import com.frappe.platform.KeyedDigests;
import com.frappe.platform.LimitKey;
import com.frappe.platform.RateLimiter;
import com.frappe.platform.Result;
import com.frappe.platform.SecretKey;
import com.frappe.platform.ShortLivedSecretStore;
import java.net.InetAddress;
import java.time.Clock;
import java.util.List;
import java.util.Locale;
import java.util.Objects;
import java.util.UUID;
import org.springframework.transaction.annotation.Transactional;

/**
 * Completes a sign-up: the holder of the mailed code chooses a password, gives their names and accepts the legal texts,
 * and becomes an active person with {@value RecoveryCodes#COUNT} recovery codes, shown once.
 *
 * <p><b>Order.</b> First the rate limit per client address ({@code CODE_CHECKS_PER_ADDRESS}), so a flood from one
 * client never reaches the breach corpus unmetered. Then everything the code does not prove (names, legal versions, the
 * password policy), so a refused password spends neither the code nor the address's tries. Then the ordering contract
 * of codes: the rate limit per address subject ({@code CODE_CHECKS_PER_SUBJECT}), then {@code consume}. Only after the
 * code proved the mailbox is the password hashed and the person stored, so nobody can set a password on an address
 * they do not hold.
 *
 * <p><b>A consumed code is gone.</b> {@code consume} settles in Valkey and cannot be undone. When the insert then fails
 * for any reason other than the address being taken (database down, a timeout), the request answers the generic 500
 * and the holder must start again for a new code. Accepted: the failure is rare and the restart is cheap.
 *
 * <p><b>One transaction around slow work (accepted).</b> The breach check (HTTP, up to 2 s), the Valkey calls and the
 * Argon2 hash run inside the use case's transaction, so a pooled connection may be held across them. A narrower shape
 * is ruled out by the conventions: a command operation is {@code @Transactional} and application code may use no other
 * Spring type ({@code TransactionTemplate} included; {@code use-cases.md}, {@code UseCaseArchitectureTests}), and doing
 * the password work in the route would put business logic in the web layer. The per-client-address limit, checked
 * before anything slow, bounds how many connections one client can hold this way. Revisit if pool saturation shows.
 *
 * <p><b>No oracle.</b> A wrong, expired, used or never-issued code, and an address with no sign-up or one that is
 * already registered (its start stored no code), all answer {@link IdentityRefusal.InvalidCode}. Only the holder of a
 * valid code can meet {@link IdentityRefusal.EmailAlreadyRegistered}: when the address was registered after the code was
 * mailed, the unique address index refuses the second person.
 */
@CommandUseCase
public class CompleteSignUp {

    /**
     * What the holder of the code submits.
     *
     * @param email the address the code was mailed to
     * @param code the code as typed
     * @param password the chosen password as typed
     * @param givenName the given name as entered
     * @param familyName the family name as entered
     * @param termsVersion the terms version accepted
     * @param privacyVersion the privacy policy version accepted
     */
    public record Request(
            EmailAddress email,
            String code,
            String password,
            String givenName,
            String familyName,
            String termsVersion,
            String privacyVersion) {

        /**
         * Validates the components.
         *
         * @param email the address, not {@code null}
         * @param code the code, not {@code null}
         * @param password the password, not {@code null}
         * @param givenName the given name, not {@code null}
         * @param familyName the family name, not {@code null}
         * @param termsVersion the terms version, not {@code null}
         * @param privacyVersion the privacy policy version, not {@code null}
         */
        public Request {
            Objects.requireNonNull(email, "email");
            Objects.requireNonNull(code, "code");
            Objects.requireNonNull(password, "password");
            Objects.requireNonNull(givenName, "givenName");
            Objects.requireNonNull(familyName, "familyName");
            Objects.requireNonNull(termsVersion, "termsVersion");
            Objects.requireNonNull(privacyVersion, "privacyVersion");
        }

        @Override
        public String toString() {
            return "Request[email=" + email + ", <redacted>]";
        }
    }

    /**
     * The new person and their recovery codes, shown once.
     *
     * @param personId the new person's id
     * @param recoveryCodes the recovery codes to show once
     */
    public record Registration(UUID personId, List<String> recoveryCodes) {

        /**
         * Copies the codes.
         *
         * @param personId the new person's id
         * @param recoveryCodes the recovery codes
         */
        public Registration {
            Objects.requireNonNull(personId, "personId");
            recoveryCodes = List.copyOf(recoveryCodes);
        }

        @Override
        public String toString() {
            return "Registration[personId=" + personId + ", recoveryCodes=<redacted>]";
        }
    }

    private final KeyedDigests digests;
    private final RateLimiter limiter;
    private final ShortLivedSecretStore store;
    private final PasswordPolicy passwordPolicy;
    private final PasswordHasher hasher;
    private final LegalVersions legalVersions;
    private final Secrets secrets;
    private final Persons persons;
    private final DomainEventPublisher events;
    private final IdGenerator ids;
    private final Clock clock;

    /**
     * Creates the use case; Spring calls it.
     *
     * @param digests subjects of addresses and digests of recovery codes
     * @param limiter the code-check rate limits
     * @param store where the code's hash lives
     * @param passwordPolicy the one password policy
     * @param hasher hashes the password
     * @param legalVersions the current legal versions
     * @param secrets new recovery codes
     * @param persons the stored people
     * @param events the outbox
     * @param ids new ids
     * @param clock the time
     */
    CompleteSignUp(
            KeyedDigests digests,
            RateLimiter limiter,
            ShortLivedSecretStore store,
            PasswordPolicy passwordPolicy,
            PasswordHasher hasher,
            LegalVersions legalVersions,
            Secrets secrets,
            Persons persons,
            DomainEventPublisher events,
            IdGenerator ids,
            Clock clock) {
        this.digests = digests;
        this.limiter = limiter;
        this.store = store;
        this.passwordPolicy = passwordPolicy;
        this.hasher = hasher;
        this.legalVersions = legalVersions;
        this.secrets = secrets;
        this.persons = persons;
        this.events = events;
        this.ids = ids;
        this.clock = clock;
    }

    /**
     * Completes the sign-up.
     *
     * @param request what was submitted
     * @param clientAddress the caller's network address, for the rate limit
     * @param locale the language the request resolved to, the person's preferred one
     * @return the registration, or why it was refused
     */
    @Transactional
    public Result<Registration, IdentityRefusal> complete(Request request, InetAddress clientAddress, Locale locale) {
        // First, so a flood from one client address cannot drive unmetered breach-corpus calls.
        if (!limiter.tryConsume(LimitKey.ofAddress(IdentityLimits.CODE_CHECKS_PER_ADDRESS, clientAddress))) {
            return Result.failure(new IdentityRefusal.TooManyAttempts());
        }
        if (!(PersonName.of(request.givenName()) instanceof Result.Success<PersonName, NameRejected>(var givenName))
                || !(PersonName.of(request.familyName())
                        instanceof Result.Success<PersonName, NameRejected>(var familyName))) {
            return Result.failure(new IdentityRefusal.InvalidName());
        }
        if (!legalVersions.areCurrent(request.termsVersion(), request.privacyVersion())) {
            return Result.failure(new IdentityRefusal.LegalTermsOutdated());
        }
        return passwordPolicy
                .check(request.password())
                .<IdentityRefusal>mapFailure(rejected -> new IdentityRefusal.PasswordRefused(rejected.reason()))
                .flatMap(password -> proveMailbox(request)
                        .flatMap(proven -> register(request, password, givenName, familyName, locale)));
    }

    // The ordering contract: the address subject's rate limit, then the code.
    private Result<EmailAddress, IdentityRefusal> proveMailbox(Request request) {
        var subject =
                digests.subjectOf(StartSignUp.EMAIL_NAMESPACE, request.email().canonical());
        if (!limiter.tryConsume(LimitKey.ofId(IdentityLimits.CODE_CHECKS_PER_SUBJECT, subject))) {
            return Result.failure(new IdentityRefusal.TooManyAttempts());
        }
        if (!store.consume(SecretKey.of(IdentitySecrets.SIGN_UP, subject), request.code())) {
            return Result.failure(new IdentityRefusal.InvalidCode());
        }
        return Result.success(request.email());
    }

    private Result<Registration, IdentityRefusal> register(
            Request request, Password password, PersonName givenName, PersonName familyName, Locale locale) {
        var now = clock.instant();
        var id = new PersonId(ids.newId());
        var issued = RecoveryCodes.issue(id, secrets, digests);
        var recoveryCodes = issued.digests().stream()
                .map(digest -> new RecoveryCode(ids.newId(), digest))
                .toList();
        var person = Person.register(
                id,
                request.email(),
                hasher.hash(password),
                givenName,
                familyName,
                locale,
                new LegalAcceptance(request.termsVersion(), request.privacyVersion(), now),
                recoveryCodes,
                now);
        return persons.add(person)
                .<IdentityRefusal>mapFailure(taken -> new IdentityRefusal.EmailAlreadyRegistered())
                .map(stored -> {
                    events.publish(new PersonRegistered(
                            ids.newId(), now, id.value(), stored.version(), PersonRegistered.VERSION));
                    events.publish(new RecoveryCodesIssued(
                            ids.newId(),
                            now,
                            id.value(),
                            stored.version(),
                            RecoveryCodesIssued.VERSION,
                            RecoveryCodesIssued.Reason.SIGN_UP));
                    return new Registration(id.value(), issued.codes());
                });
    }
}
