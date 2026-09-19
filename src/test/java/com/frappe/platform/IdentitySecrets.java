package com.frappe.platform;

/** What a module's secret purposes look like: one enum per module, the module name once. */
public enum IdentitySecrets implements SecretPurpose {
    EMAIL_PROOF,
    RECOVERY;

    @Override
    public String module() {
        return "identity";
    }
}
