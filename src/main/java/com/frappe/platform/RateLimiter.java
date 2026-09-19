package com.frappe.platform;

/**
 * Token-bucket rate limits shared by all instances of the API, e.g. login and recovery attempts per account and per IP
 * address. A limit per instance would multiply with the number of instances.
 */
public interface RateLimiter {

    /**
     * Takes one token from the key's bucket, creating a full bucket on first use.
     *
     * @param key the limited subject and the limit's definition
     * @return {@code true} if the call is allowed; {@code false} if the bucket is empty
     * @throws SecretStoreUnavailableException if the store cannot be reached
     */
    boolean tryConsume(LimitKey key);
}
