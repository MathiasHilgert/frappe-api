package ratelimit

import (
	"context"
	"errors"
	"fmt"
	"strconv"
	"time"

	valkeygo "github.com/valkey-io/valkey-go"
)

// Defaults applied by New to zero-valued Settings fields.
const (
	DefaultKeyPrefix = "frappe:rate_limit:"
	DefaultTimeout   = 250 * time.Millisecond
)

// gcraScript atomically applies one GCRA step to KEYS[1]. ARGV[1] is the
// emission interval and ARGV[2] the burst tolerance (the window), both in
// microseconds. The clock is the server's own TIME, in microseconds.
// Stored values are formatted with %.0f so microsecond timestamps (about
// 1.8e15, below 2^53) keep full precision. It returns {allowed (0 or 1),
// remaining, retry_after_microseconds, reset_after_microseconds}.
var gcraScript = valkeygo.NewLuaScript(`
local emission = tonumber(ARGV[1])
local tolerance = tonumber(ARGV[2])
local clock = redis.call('TIME')
local now = tonumber(clock[1]) * 1000000 + tonumber(clock[2])
local tat = tonumber(redis.call('GET', KEYS[1]))
if not tat or tat < now then
  tat = now
end
local new_tat = tat + emission
local allow_at = new_tat - tolerance
if now < allow_at then
  return {0, 0, allow_at - now, tat - now}
end
redis.call('SET', KEYS[1], string.format('%.0f', new_tat), 'PX', math.ceil((new_tat - now) / 1000))
return {1, math.floor((tolerance - (new_tat - now)) / emission), 0, new_tat - now}
`)

// Settings configures a Limiter.
type Settings struct {
	// KeyPrefix namespaces every key in Valkey. Defaults to
	// DefaultKeyPrefix when empty.
	KeyPrefix string
	// Window is the period Requests are allowed in. Required, positive.
	Window time.Duration
	// Timeout bounds one Allow round trip, so an unhealthy Valkey adds at
	// most this much latency before the caller fails open. Defaults to
	// DefaultTimeout when zero.
	Timeout time.Duration
	// Requests is how many requests are allowed per Window. Required,
	// positive.
	Requests int
}

// Decision is the outcome of one Allow call.
type Decision struct {
	// ResetAfter is how long until the full quota is restored.
	ResetAfter time.Duration
	// RetryAfter is how long until the next request would be allowed;
	// zero when Allowed.
	RetryAfter time.Duration
	// Limit is Settings.Requests.
	Limit int
	// Remaining is how many more requests are allowed right now.
	Remaining int
	// Allowed reports whether the request may proceed.
	Allowed bool
}

// Limiter is a GCRA rate limiter backed by Valkey. It is safe for
// concurrent use, and any number of Limiters (on any number of replicas)
// sharing one Valkey and KeyPrefix enforce one shared limit.
type Limiter struct {
	client    valkeygo.Client
	keyPrefix string
	emission  string
	tolerance string
	timeout   time.Duration
	requests  int
}

// New validates settings and builds a Limiter over client.
func New(client valkeygo.Client, settings Settings) (*Limiter, error) {
	if settings.Requests <= 0 {
		return nil, fmt.Errorf("ratelimit: requests must be positive, got %d", settings.Requests)
	}
	if settings.Window <= 0 {
		return nil, fmt.Errorf("ratelimit: window must be positive, got %s", settings.Window)
	}
	if settings.Timeout < 0 {
		return nil, fmt.Errorf("ratelimit: timeout must not be negative, got %s", settings.Timeout)
	}
	emission := settings.Window.Microseconds() / int64(settings.Requests)
	if emission < 1 {
		return nil, errors.New("ratelimit: window divided by requests must be at least one microsecond")
	}
	if settings.KeyPrefix == "" {
		settings.KeyPrefix = DefaultKeyPrefix
	}
	if settings.Timeout == 0 {
		settings.Timeout = DefaultTimeout
	}

	return &Limiter{
		client:    client,
		keyPrefix: settings.KeyPrefix,
		emission:  strconv.FormatInt(emission, 10),
		tolerance: strconv.FormatInt(settings.Window.Microseconds(), 10),
		timeout:   settings.Timeout,
		requests:  settings.Requests,
	}, nil
}

// Allow consumes one request for key, if the quota allows it.
func (limiter *Limiter) Allow(ctx context.Context, key string) (Decision, error) {
	ctx, cancel := context.WithTimeout(ctx, limiter.timeout)
	defer cancel()

	values, err := gcraScript.Exec(ctx, limiter.client,
		[]string{limiter.keyPrefix + key},
		[]string{limiter.emission, limiter.tolerance},
	).AsIntSlice()
	if err != nil {
		return Decision{}, fmt.Errorf("ratelimit: run script: %w", err)
	}
	if len(values) != 4 {
		return Decision{}, fmt.Errorf("ratelimit: unexpected script result length %d", len(values))
	}

	return Decision{
		Allowed:    values[0] == 1,
		Limit:      limiter.requests,
		Remaining:  int(values[1]),
		RetryAfter: time.Duration(values[2]) * time.Microsecond,
		ResetAfter: time.Duration(values[3]) * time.Microsecond,
	}, nil
}
