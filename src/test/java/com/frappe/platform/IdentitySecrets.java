package com.frappe.platform;

import java.time.Duration;

/**
 * What a module's secret purposes look like: one enum per module, the module name once, and each constant carrying its
 * lifetime and issue cap, so call sites repeat no numbers.
 */
public enum IdentitySecrets implements SecretPurpose {
    EMAIL_PROOF(Duration.ofMinutes(15), Duration.ofHours(1), 5),
    RECOVERY(Duration.ofMinutes(30), Duration.ofHours(24), 3);

    private final Duration ttl;

    private final Duration issueWindow;

    private final int issueLimit;

    IdentitySecrets(Duration ttl, Duration issueWindow, int issueLimit) {
        this.ttl = ttl;
        this.issueWindow = issueWindow;
        this.issueLimit = issueLimit;
    }

    @Override
    public String module() {
        return "identity";
    }

    @Override
    public Duration ttl() {
        return ttl;
    }

    @Override
    public Duration issueWindow() {
        return issueWindow;
    }

    @Override
    public int issueLimit() {
        return issueLimit;
    }
}
