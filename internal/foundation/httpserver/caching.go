package httpserver

import (
	"context"
	"crypto/sha256"
	"encoding/base64"
	"net/http"
	"strconv"
	"strings"
	"time"

	"go.opentelemetry.io/otel/metric"
	semconv "go.opentelemetry.io/otel/semconv/v1.43.0"
)

// Caching response and request headers (RFC 9110, RFC 9111).
const (
	CacheControlHeader = "Cache-Control"
	ETagHeader         = "ETag"
	VaryHeader         = "Vary"
	IfNoneMatchHeader  = "If-None-Match"
)

// noStoreDirective is the Cache-Control value of every /v1 response whose
// handler declared no policy, and of every error response.
const noStoreDirective = "no-store"

// CachePolicy is a per-response HTTP cache policy a module adapter hands
// to NotModified. Build one with NoStore, Private, Public or Revalidate;
// the zero value behaves like NoStore.
type CachePolicy struct {
	directive  string
	varyTenant bool
}

// NoStore forbids every cache from storing the response. It is the
// default for /v1 responses.
func NoStore() CachePolicy {
	return CachePolicy{directive: noStoreDirective}
}

// Private lets only the client's own cache store the response, for
// maxAge. The response varies on Authorization and on the configured
// tenant headers so no cache ever serves one caller's data to another.
func Private(maxAge time.Duration) CachePolicy {
	return CachePolicy{directive: "private, max-age=" + seconds(maxAge), varyTenant: true}
}

// Public lets shared caches (proxies, CDNs) store the response for maxAge
// and serve it stale for staleWhileRevalidate while revalidating. Only use
// it for data that is identical for every caller and tenant: it adds no
// Vary on Authorization.
func Public(maxAge, staleWhileRevalidate time.Duration) CachePolicy {
	directive := "public, max-age=" + seconds(maxAge)
	if staleWhileRevalidate > 0 {
		directive += ", stale-while-revalidate=" + seconds(staleWhileRevalidate)
	}
	return CachePolicy{directive: directive}
}

// Revalidate lets the client store the response but requires it to
// revalidate (If-None-Match) before every reuse ("no-cache").
func Revalidate() CachePolicy {
	return CachePolicy{directive: "no-cache", varyTenant: true}
}

// CacheControl returns the policy's Cache-Control header value.
func (policy CachePolicy) CacheControl() string {
	if policy.directive == "" {
		return noStoreDirective
	}
	return policy.directive
}

func seconds(duration time.Duration) string {
	return strconv.FormatInt(int64(duration/time.Second), 10)
}

// ETagFromVersion returns a strong ETag for version of entity, for example
// ETagFromVersion("orders/42", order.Version) or an updated_at in
// nanoseconds. It is the cheap, preferred form: the handler can compare it
// before loading or serializing the body. entity is base64url encoded, so
// any string is safe and distinct entities never collide.
func ETagFromVersion(entity string, version int64) string {
	return `"` + base64.RawURLEncoding.EncodeToString([]byte(entity)) + "." + strconv.FormatInt(version, 10) + `"`
}

// ETagFromBody returns a strong ETag from the SHA-256 of a deterministic
// representation (for example the marshaled body).
func ETagFromBody(body []byte) string {
	sum := sha256.Sum256(body)
	return `"` + base64.RawURLEncoding.EncodeToString(sum[:]) + `"`
}

// cacheState is the per-request caching state the caching middleware puts
// in the request context and NotModified fills in.
type cacheState struct {
	ifNoneMatch string
	etag        string
	policy      CachePolicy
	conditional bool
	declared    bool
}

type cacheStateKey struct{}

// NotModified declares etag and policy for the current response and
// reports whether the request is a GET or HEAD whose If-None-Match matches
// etag (weak comparison, RFC 9110 section 13.1.2). When it returns true the
// handler must stop and return huma.Status304NotModified(); the response
// then carries ETag and Cache-Control but no body. Call it before
// computing the body. Outside the /v1 middleware chain it returns false.
func NotModified(ctx context.Context, etag string, policy CachePolicy) bool {
	state, ok := ctx.Value(cacheStateKey{}).(*cacheState)
	if !ok {
		return false
	}
	state.etag = etag
	state.policy = policy
	state.declared = true
	return state.conditional && ifNoneMatchMatches(state.ifNoneMatch, etag)
}

// ifNoneMatchMatches reports whether header ("*" or a comma separated
// list of entity tags) matches etag under weak comparison.
func ifNoneMatchMatches(header, etag string) bool {
	if etag == "" {
		return false
	}
	header = strings.TrimSpace(header)
	if header == "*" {
		return true
	}
	opaque := strings.TrimPrefix(etag, "W/")
	for header != "" {
		candidate, rest, ok := nextEntityTag(header)
		if !ok {
			return false
		}
		if candidate == opaque {
			return true
		}
		header = rest
	}
	return false
}

// nextEntityTag consumes one entity tag from list, returning its quoted
// opaque part (without W/) and the remainder after the next comma.
func nextEntityTag(list string) (string, string, bool) {
	list = strings.TrimLeft(list, " \t,")
	if list == "" {
		return "", "", false
	}
	list = strings.TrimPrefix(list, "W/")
	if !strings.HasPrefix(list, `"`) {
		return "", "", false
	}
	end := strings.IndexByte(list[1:], '"')
	if end < 0 {
		return "", "", false
	}
	tag := list[:end+2]
	rest := strings.TrimLeft(list[end+2:], " \t")
	if rest != "" && rest[0] != ',' {
		return "", "", false
	}
	return tag, strings.TrimPrefix(rest, ","), true
}

var notModifiedResponses, _ = meter.Int64Counter(
	"frappe.http.not_modified",
	metric.WithDescription("Responses answered with 304 Not Modified, by http.route"),
)

// cachingMiddleware installs a cacheState on every /v1 request and applies
// it when the response header is written: the declared policy and ETag on
// a 2xx or 304, no-store otherwise, plus Vary on Authorization and
// varyHeaders for tenant-scoped policies.
func cachingMiddleware(varyHeaders []string, apiMux *http.ServeMux) func(http.Handler) http.Handler {
	return func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			if !strings.HasPrefix(r.URL.Path, versionedAPIPrefix+"/") {
				next.ServeHTTP(w, r)
				return
			}
			state := &cacheState{
				ifNoneMatch: r.Header.Get(IfNoneMatchHeader),
				conditional: r.Method == http.MethodGet || r.Method == http.MethodHead,
			}
			writer := &cacheWriter{ResponseWriter: w, state: state, varyHeaders: varyHeaders, request: r, apiMux: apiMux}
			next.ServeHTTP(writer, r.WithContext(context.WithValue(r.Context(), cacheStateKey{}, state)))
		})
	}
}

// cacheWriter applies the request's cacheState right before the response
// header is sent.
type cacheWriter struct {
	http.ResponseWriter
	state       *cacheState
	request     *http.Request
	apiMux      *http.ServeMux
	varyHeaders []string
	applied     bool
}

func (writer *cacheWriter) WriteHeader(status int) {
	writer.apply(status)
	writer.ResponseWriter.WriteHeader(status)
}

func (writer *cacheWriter) Write(body []byte) (int, error) {
	writer.apply(http.StatusOK)
	return writer.ResponseWriter.Write(body)
}

// Unwrap exposes the wrapped writer to http.ResponseController.
func (writer *cacheWriter) Unwrap() http.ResponseWriter {
	return writer.ResponseWriter
}

func (writer *cacheWriter) apply(status int) {
	if writer.applied {
		return
	}
	writer.applied = true
	header := writer.Header()
	cacheable := writer.state.declared && (status == http.StatusNotModified || (status >= 200 && status < 300))
	if !cacheable {
		if header.Get(CacheControlHeader) == "" {
			header.Set(CacheControlHeader, noStoreDirective)
		}
		return
	}
	writer.applyPolicy(status, header)
}

// applyPolicy writes the declared policy, ETag and Vary headers of a
// cacheable (2xx or 304) response.
func (writer *cacheWriter) applyPolicy(status int, header http.Header) {
	header.Set(CacheControlHeader, writer.state.policy.CacheControl())
	if writer.state.etag != "" {
		header.Set(ETagHeader, writer.state.etag)
	}
	if writer.state.policy.varyTenant {
		header.Add(VaryHeader, "Authorization")
		for _, name := range writer.varyHeaders {
			header.Add(VaryHeader, name)
		}
	}
	if status == http.StatusNotModified {
		header.Del("Content-Type")
		header.Del("Content-Length")
		notModifiedResponses.Add(writer.request.Context(), 1,
			metric.WithAttributes(semconv.HTTPRoute(matchedRoute(writer.apiMux, writer.request))))
	}
}
