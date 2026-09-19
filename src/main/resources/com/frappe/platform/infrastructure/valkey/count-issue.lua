-- Sliding-window log: records one issue unless the key already holds the limit within the window.
-- KEYS[1]: sorted set of issues, scored by issue time in epoch milliseconds
-- ARGV[1]: now (epoch milliseconds, application clock)
-- ARGV[2]: window length in milliseconds
-- ARGV[3]: issues allowed per window
-- ARGV[4]: unique member for this issue
-- Returns 1 if the issue was recorded, 0 if it was refused.
local now = tonumber(ARGV[1])
local window = tonumber(ARGV[2])
-- An issue leaves the window once it is window milliseconds old.
redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', now - window)
if redis.call('ZCARD', KEYS[1]) >= tonumber(ARGV[3]) then
    return 0
end
redis.call('ZADD', KEYS[1], now, ARGV[4])
-- Every entry is outside the window once the newest one is: the log removes itself.
redis.call('PEXPIRE', KEYS[1], window)
return 1
