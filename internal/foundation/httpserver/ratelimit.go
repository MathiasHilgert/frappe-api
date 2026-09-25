package httpserver

import (
	"context"
	"errors"
	"log/slog"
	"math"
	"net"
	"net/http"
	"net/netip"
	"strconv"
	"strings"
	"sync"
	"time"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/metric"
	semconv "go.opentelemetry.io/otel/semconv/v1.43.0"
)

// Rate limit response headers. RateLimit-Limit, RateLimit-Remaining and
// RateLimit-Reset follow the IETF "RateLimit header fields for HTTP"
// draft (draft-ietf-httpapi-ratelimit-headers, the widely deployed
// separate-field form); Retry-After is RFC 9110. Every value is an
// integer: a count, or delta seconds rounded up.
const (
	RateLimitLimitHeader     = "RateLimit-Limit"
	RateLimitRemainingHeader = "RateLimit-Remaining"
	RateLimitResetHeader     = "RateLimit-Reset"
	RetryAfterHeader         = "Retry-After"
)

// versionedAPIPrefix is the only path prefix rate limiting applies to.
// Health probes are mounted outside the middleware chain entirely, and
// the OpenAPI documentation (/docs, /openapi.*, /schemas) lives outside
// it, so neither is ever limited.
const versionedAPIPrefix = "/v1"

// rateLimitErrorLogInterval bounds how often a failing limiter is logged,
// so an unavailable Valkey does not turn every request into a log line.
const rateLimitErrorLogInterval = 30 * time.Second

// RateLimitDecision is the outcome of one RateLimiter.Allow call.
type RateLimitDecision struct {
	// ResetAfter is how long until the quota is fully restored.
	ResetAfter time.Duration
	// RetryAfter is how long until the next request would be allowed. It
	// is only meaningful when Allowed is false.
	RetryAfter time.Duration
	// Limit is the number of requests allowed per window.
	Limit int
	// Remaining is the number of requests still allowed right now.
	Remaining int
	// Allowed reports whether the request may proceed.
	Allowed bool
}

// RateLimiter decides whether one more request identified by key may
// proceed. It is a consumer-side interface: this package never knows the
// storage behind it (internal/foundation/ratelimit's Valkey limiter,
// adapted by the composition root, in production).
type RateLimiter interface {
	Allow(ctx context.Context, key string) (RateLimitDecision, error)
}

// RateLimitSettings configures rate limiting of the /v1 API. The zero
// value (a nil Limiter) disables it.
type RateLimitSettings struct {
	// Limiter decides every request. Nil disables rate limiting.
	Limiter RateLimiter
	// KeyFunction derives the rate limit key for a request. Nil uses
	// ClientAddressKey(TrustedProxies). Swap it to limit per tenant or
	// per user once authentication exists.
	KeyFunction func(*http.Request) string
	// TrustedProxies lists the proxy networks whose X-Forwarded-For
	// header is trusted by the default key function.
	TrustedProxies []netip.Prefix
}

// meter is the global OpenTelemetry meter, obtained through the
// OpenTelemetry API only, so this package never imports the telemetry SDK.
var meter = otel.Meter("github.com/MathiasHilgert/frappe-api/internal/foundation/httpserver")

// Rate limit metric attribute values. Every rate limit metric carries
// only these bounded values plus http.route (the matched route template,
// or unmatchedRoute): never the client address, the rate limit key or the
// raw request path, which would give each metric unbounded cardinality.
const (
	rateLimitOutcomeAllowed = "allowed"
	rateLimitOutcomeLimited = "limited"
	rateLimitOutcomeError   = "error"
	rateLimitReasonTimeout  = "timeout"
	rateLimitReasonError    = "error"
)

var (
	rateLimitDecisions, _ = meter.Int64Counter(
		"frappe.rate_limit.decisions",
		metric.WithDescription("Rate limit decisions, by outcome (allowed or limited) and http.route"),
	)
	rateLimitErrors, _ = meter.Int64Counter(
		"frappe.rate_limit.errors",
		metric.WithDescription("Rate limiter failures, by reason (timeout or error) and http.route; the request was allowed (fail open)"),
	)
	rateLimitDuration, _ = meter.Float64Histogram(
		"frappe.rate_limit.duration",
		metric.WithDescription("Latency of one rate limiter Allow call, by outcome (allowed, limited or error) and http.route"),
		metric.WithUnit("s"),
	)
)

// rateLimitMiddleware enforces settings.Limiter on /v1 requests. It fails
// open: a limiter error lets the request through (the limiter protects
// capacity and must never cause an outage itself), records
// frappe.rate_limit.errors and logs a warning at most once per
// rateLimitErrorLogInterval. CORS preflights are always skipped. apiMux
// resolves the http.route metric attribute.
func rateLimitMiddleware(settings RateLimitSettings, logger *slog.Logger, apiMux *http.ServeMux) func(http.Handler) http.Handler {
	if settings.Limiter == nil {
		return func(next http.Handler) http.Handler { return next }
	}
	keyFunction := settings.KeyFunction
	if keyFunction == nil {
		keyFunction = ClientAddressKey(settings.TrustedProxies)
	}
	errorLog := &throttledLog{logger: logger, interval: rateLimitErrorLogInterval}

	return func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			if !isRateLimited(r) {
				next.ServeHTTP(w, r)
				return
			}

			route := semconv.HTTPRoute(matchedRoute(apiMux, r))
			started := time.Now()
			decision, err := settings.Limiter.Allow(r.Context(), keyFunction(r))
			elapsed := time.Since(started)
			if err != nil {
				recordRateLimitError(r.Context(), route, elapsed, err)
				errorLog.warn(r.Context(), err)
				next.ServeHTTP(w, r)
				return
			}

			writeRateLimitHeaders(w.Header(), decision)
			if !decision.Allowed {
				recordDecision(r.Context(), route, elapsed, rateLimitOutcomeLimited)
				w.Header().Set(RetryAfterHeader, deltaSeconds(decision.RetryAfter))
				writeProblem(w, http.StatusTooManyRequests, "Too Many Requests")
				return
			}
			recordDecision(r.Context(), route, elapsed, rateLimitOutcomeAllowed)
			next.ServeHTTP(w, r)
		})
	}
}

// isRateLimited reports whether r targets the versioned API and is not a
// CORS preflight.
func isRateLimited(r *http.Request) bool {
	if r.Method == http.MethodOptions && r.Header.Get("Access-Control-Request-Method") != "" {
		return false
	}
	return r.URL.Path == versionedAPIPrefix || strings.HasPrefix(r.URL.Path, versionedAPIPrefix+"/")
}

func recordDecision(ctx context.Context, route attribute.KeyValue, elapsed time.Duration, outcome string) {
	attributes := metric.WithAttributes(attribute.String("outcome", outcome), route)
	rateLimitDecisions.Add(ctx, 1, attributes)
	rateLimitDuration.Record(ctx, elapsed.Seconds(), attributes)
}

func recordRateLimitError(ctx context.Context, route attribute.KeyValue, elapsed time.Duration, err error) {
	rateLimitErrors.Add(ctx, 1, metric.WithAttributes(attribute.String("reason", rateLimitErrorReason(err)), route))
	rateLimitDuration.Record(ctx, elapsed.Seconds(),
		metric.WithAttributes(attribute.String("outcome", rateLimitOutcomeError), route))
}

// rateLimitErrorReason classifies a limiter error: "timeout" when the
// Allow call ran out of time (its context deadline, bounded by
// RATE_LIMIT_TIMEOUT, or a network timeout), "error" for anything else.
func rateLimitErrorReason(err error) string {
	if errors.Is(err, context.DeadlineExceeded) {
		return rateLimitReasonTimeout
	}
	var networkError net.Error
	if errors.As(err, &networkError) && networkError.Timeout() {
		return rateLimitReasonTimeout
	}
	return rateLimitReasonError
}

func writeRateLimitHeaders(header http.Header, decision RateLimitDecision) {
	header.Set(RateLimitLimitHeader, strconv.Itoa(decision.Limit))
	header.Set(RateLimitRemainingHeader, strconv.Itoa(max(decision.Remaining, 0)))
	header.Set(RateLimitResetHeader, deltaSeconds(decision.ResetAfter))
}

// deltaSeconds renders duration as whole seconds, rounded up so a client
// never retries too early.
func deltaSeconds(duration time.Duration) string {
	return strconv.FormatInt(int64(math.Ceil(max(duration, 0).Seconds())), 10)
}

// ClientAddressKey returns the default rate limit key function: "ip:"
// plus the client address. The client address is the connection's
// RemoteAddr, unless RemoteAddr belongs to trustedProxies: then
// X-Forwarded-For is walked from right to left, skipping trusted proxy
// hops, and the first untrusted address is used. The leftmost entries are
// never trusted blindly, since any client can send them. Without trusted
// proxies X-Forwarded-For is ignored entirely.
func ClientAddressKey(trustedProxies []netip.Prefix) func(*http.Request) string {
	return func(r *http.Request) string {
		remote := remoteAddress(r)
		if !remote.IsValid() {
			return "ip:" + r.RemoteAddr
		}
		if isTrusted(remote, trustedProxies) {
			if forwarded, found := forwardedClient(r, trustedProxies); found {
				return "ip:" + forwarded.String()
			}
		}
		return "ip:" + remote.String()
	}
}

func remoteAddress(r *http.Request) netip.Addr {
	host, _, err := net.SplitHostPort(r.RemoteAddr)
	if err != nil {
		host = r.RemoteAddr
	}
	address, err := netip.ParseAddr(host)
	if err != nil {
		return netip.Addr{}
	}
	return address.Unmap()
}

// forwardedClient walks X-Forwarded-For from right to left and returns
// the first address not in trustedProxies. An unparsable entry stops the
// walk (found is false), since nothing to its left can be trusted.
func forwardedClient(r *http.Request, trustedProxies []netip.Prefix) (netip.Addr, bool) {
	entries := strings.Split(strings.Join(r.Header.Values("X-Forwarded-For"), ","), ",")
	for index := len(entries) - 1; index >= 0; index-- {
		entry := strings.TrimSpace(entries[index])
		if entry == "" {
			continue
		}
		address, err := netip.ParseAddr(entry)
		if err != nil {
			return netip.Addr{}, false
		}
		address = address.Unmap()
		if !isTrusted(address, trustedProxies) {
			return address, true
		}
	}
	return netip.Addr{}, false
}

func isTrusted(address netip.Addr, trustedProxies []netip.Prefix) bool {
	for _, prefix := range trustedProxies {
		if prefix.Contains(address) {
			return true
		}
	}
	return false
}

// throttledLog logs a warning at most once per interval, reporting how
// many occurrences were suppressed in between.
type throttledLog struct {
	last       time.Time
	logger     *slog.Logger
	interval   time.Duration
	suppressed int
	mutex      sync.Mutex
}

func (log *throttledLog) warn(ctx context.Context, err error) {
	log.mutex.Lock()
	now := time.Now()
	if !log.last.IsZero() && now.Sub(log.last) < log.interval {
		log.suppressed++
		log.mutex.Unlock()
		return
	}
	suppressed := log.suppressed
	log.last = now
	log.suppressed = 0
	log.mutex.Unlock()

	log.logger.WarnContext(ctx, "rate limiter unavailable, allowing request (fail open)",
		slog.Any("error", err),
		slog.Int("suppressed_since_last_warning", suppressed),
	)
}
