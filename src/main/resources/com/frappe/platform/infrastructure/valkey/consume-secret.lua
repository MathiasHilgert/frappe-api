-- Settles one attempt against a short-lived secret atomically, after the caller verified the candidate against the
-- stored Argon2 hash (salted hashes cannot be compared inside Valkey).
-- KEYS[1]: the secret hash (fields "hash" and "failures")
-- ARGV[1]: the stored hash the caller verified against
-- ARGV[2]: "1" if the candidate matched, "0" otherwise
-- ARGV[3]: failed attempts after which the secret is deleted
-- Returns 1 if this attempt consumed the secret, 0 otherwise.
if redis.call('HGET', KEYS[1], 'hash') ~= ARGV[1] then
    -- Consumed, expired, deleted or replaced since the caller read it: never counts, never succeeds twice.
    return 0
end
if ARGV[2] == '1' then
    redis.call('DEL', KEYS[1])
    return 1
end
if redis.call('HINCRBY', KEYS[1], 'failures', 1) >= tonumber(ARGV[3]) then
    redis.call('DEL', KEYS[1])
end
return 0
