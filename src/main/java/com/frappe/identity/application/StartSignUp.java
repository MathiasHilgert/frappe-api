package com.frappe.identity.application;

import com.frappe.identity.IdentityLimits;
import com.frappe.identity.IdentitySecrets;
import com.frappe.identity.domain.EmailAddress;
import com.frappe.identity.domain.SignUp;
import com.frappe.identity.domain.SignUpCapped;
import com.frappe.identity.domain.SignUpId;
import com.frappe.identity.domain.SignUps;
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
import java.util.Locale;
import org.springframework.transaction.annotation.Transactional;

/**
 * Starts a sign-up: records the address and publishes {@code SignUpStarted}, so identity's listener mails a code after
 * the commit.
 *
 * <p><b>Same answer, same work.</b> Every accepted start costs a keyed digest, the rate limit, the issue cap and one
 * upsert statement by subject; no password hashing, no mail, no person lookup, and no branch on whether the address
 * already has a sign-up. So neither the answer nor its timing reveals whether an address is registered, started or
 * capped, and concurrent starts for one address all succeed.
 *
 * <p><b>The cap is spent before the commit.</b> {@code countIssue} counts in Valkey before the database writes; if the
 * upsert or the commit then fails (the caller gets the generic 500), that issue stays spent without a code. Accepted:
 * the cap refills within its window, and an uncounted retry path would let a failing database mint unlimited codes.
 *
 * <p><b>Victim denial (accepted by design).</b> Anyone may spend an address's issue cap, 5 codes per hour, and its owner
 * then gets no code for up to an hour, with the same 202. Answering differently would reveal the address's state. Capped
 * starts are counted as {@code frappe.identity.sign_ups.capped} (no tags), so such abuse shows on dashboards; the
 * per-client rate limit bounds how fast one client can do it.
 *
 * <p><b>Valkey inside the transaction.</b> The rate limit and the issue cap run inside the use case's transaction, so a
 * pooled connection may be held across the two Valkey calls (each bounded by {@code spring.data.redis.timeout}, 2 s).
 * Moving them out would need a command use case without a transaction or a second use case called from the first,
 * which {@code use-cases.md} (every command operation is {@code @Transactional}; one operation per use case, called by
 * adapters) and {@code UseCaseArchitectureTests} rule out. Revisit if pool saturation shows under abuse.
 */
@CommandUseCase
public class StartSignUp {

    /** The {@link KeyedDigests} namespace of canonical email addresses. */
    public static final String EMAIL_NAMESPACE = "identity.email";

    /** How an accepted start ended; the caller answers both alike. */
    public enum Outcome {
        /** The sign-up was recorded and a code is to be mailed. */
        STARTED,
        /** The address reached its issue cap: nothing was written and no code is mailed. */
        CAPPED
    }

    private final KeyedDigests digests;
    private final RateLimiter limiter;
    private final ShortLivedSecretStore secrets;
    private final SignUps signUps;
    private final DomainEventPublisher events;
    private final IdGenerator ids;
    private final Clock clock;

    /**
     * Creates the use case; Spring calls it.
     *
     * @param digests subjects of addresses
     * @param limiter the per-address rate limit
     * @param secrets the issue cap
     * @param signUps the stored sign-ups
     * @param events the outbox
     * @param ids new ids
     * @param clock the time
     */
    StartSignUp(
            KeyedDigests digests,
            RateLimiter limiter,
            ShortLivedSecretStore secrets,
            SignUps signUps,
            DomainEventPublisher events,
            IdGenerator ids,
            Clock clock) {
        this.digests = digests;
        this.limiter = limiter;
        this.secrets = secrets;
        this.signUps = signUps;
        this.events = events;
        this.ids = ids;
        this.clock = clock;
    }

    /**
     * Starts or restarts the sign-up of an address: rate limit per client address, then the address's issue cap, then
     * the sign-up is recorded. A capped address is accepted without writing a sign-up; only its count is recorded.
     *
     * @param email the address as entered
     * @param clientAddress the caller's network address, for the rate limit
     * @param locale the language to mail in
     * @return the outcome, or {@link IdentityRefusal.TooManyAttempts} when the client address is rate limited
     */
    @Transactional
    public Result<Outcome, IdentityRefusal> start(EmailAddress email, InetAddress clientAddress, Locale locale) {
        var subject = digests.subjectOf(EMAIL_NAMESPACE, email.canonical());
        if (!limiter.tryConsume(LimitKey.ofAddress(IdentityLimits.SIGN_UP_PER_ADDRESS, clientAddress))) {
            return Result.failure(new IdentityRefusal.TooManyAttempts());
        }
        // Counted here, never in the listener: a retried mail must not spend the cap.
        if (!secrets.countIssue(SecretKey.of(IdentitySecrets.SIGN_UP, subject))) {
            events.publish(SignUpCapped.of(ids.newId(), clock.instant()));
            return Result.success(Outcome.CAPPED);
        }
        var stored = signUps.record(SignUp.start(new SignUpId(ids.newId()), email, subject, locale, clock.instant()));
        events.publish(stored.started(ids.newId()));
        return Result.success(Outcome.STARTED);
    }
}
