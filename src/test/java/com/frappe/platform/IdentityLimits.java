package com.frappe.platform;

import java.time.Duration;

/** What a module's rate limits look like: each constant carries its definition, so call sites repeat no numbers. */
public enum IdentityLimits implements LimitPurpose {
    LOGIN_PER_ACCOUNT(3, Duration.ofMinutes(1)),
    LOGIN_PER_ADDRESS(20, Duration.ofMinutes(1));

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
