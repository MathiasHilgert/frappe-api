-- Replaces a secret atomically: a secret never exists without its TTL, and never keeps an earlier failure count.
-- KEYS[1]: the secret hash (fields "hash" and "failures")
-- ARGV[1]: the Argon2 hash of the new secret
-- ARGV[2]: time to live in milliseconds
redis.call('DEL', KEYS[1])
redis.call('HSET', KEYS[1], 'hash', ARGV[1], 'failures', 0)
redis.call('PEXPIRE', KEYS[1], ARGV[2])
return 1
