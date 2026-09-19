package com.frappe.identity;

import com.frappe.platform.SecretPurpose;
import java.time.Duration;

/** identity's one-time codes: how long each lives and how many may be issued per subject in a window. */
public enum IdentitySecrets implements SecretPurpose {
    //       ttl                     issue window         issue limit
    /** The code proving the mailbox of a new account, keyed by the address's subject. */
    SIGN_UP(Duration.ofMinutes(15), Duration.ofHours(1), 5);

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
