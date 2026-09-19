package com.frappe.identity.application;

import com.frappe.identity.IdentityLimits;
import com.frappe.identity.IdentitySecrets;
import com.frappe.identity.domain.EmailAddress;
import com.frappe.identity.domain.SignUp;
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
 * the commit. The work is the same for every address (a keyed digest, the rate limit, the issue cap and one row by
 * subject; no password hashing, no mail, no person lookup), so neither the answer nor its timing reveals whether the
 * address is registered.
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
     * the sign-up is recorded. A capped address is accepted without writing anything.
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
            return Result.success(Outcome.CAPPED);
        }
        var now = clock.instant();
        var signUp = signUps.byEmailSubject(subject)
                .map(existing -> {
                    existing.restart(email, locale, now, ids.newId());
                    return existing;
                })
                .orElseGet(() -> SignUp.start(new SignUpId(ids.newId()), email, subject, locale, now, ids.newId()));
        signUps.save(signUp);
        events.publishAll(signUp.pullEvents());
        return Result.success(Outcome.STARTED);
    }
}
