package com.frappe.identity;

import com.frappe.platform.LimitPurpose;
import java.time.Duration;

/** identity's rate limits: how many calls a subject may make per period. */
public enum IdentityLimits implements LimitPurpose {
    /** Sign-up starts per client address. */
    SIGN_UP_PER_ADDRESS(20, Duration.ofHours(1)),
    /** Code checks per subject (an address's keyed digest, or a person id). */
    CODE_CHECKS_PER_SUBJECT(10, Duration.ofMinutes(15)),
    /** Code checks per client address. */
    CODE_CHECKS_PER_ADDRESS(50, Duration.ofMinutes(15));

    private final long capacity;
    private final Duration period;

    IdentityLimits(long capacity, Duration period) {
        this.capacity = capacity;
        this.period = period;
    }

    @Override
    public String module() {
        return "identity";
    }

    @Override
    public long capacity() {
        return capacity;
    }

    @Override
    public Duration period() {
        return period;
    }
}
