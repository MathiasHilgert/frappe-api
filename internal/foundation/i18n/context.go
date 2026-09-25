package i18n

import (
	"context"
	"net/http"
)

// Response headers set by Middleware (RFC 9110).
const (
	AcceptLanguageHeader  = "Accept-Language"
	ContentLanguageHeader = "Content-Language"
	varyHeader            = "Vary"
)

type localizedKey struct{}

// localized is what WithLocale stores in a context.
type localized struct {
	catalog *Catalog
	locale  Locale
}

// WithLocale returns a copy of ctx that carries locale and this catalog,
// for code outside an HTTP request (for example an event consumer) that
// needs T or TranslateWith.
func (catalog *Catalog) WithLocale(ctx context.Context, locale Locale) context.Context {
	return context.WithValue(ctx, localizedKey{}, localized{catalog: catalog, locale: locale})
}

// FromContext returns the locale negotiated for ctx by Middleware (or set
// by WithLocale), and false when there is none.
func FromContext(ctx context.Context) (Locale, bool) {
	value, ok := ctx.Value(localizedKey{}).(localized)
	return value.locale, ok
}

// T renders key in the locale carried by ctx. See TranslateWith.
func T(ctx context.Context, key Key) string {
	return TranslateWith(ctx, key, nil)
}

// TranslateWith renders key with data in the locale carried by ctx,
// falling back to the source locale and then to the key itself. Without a
// localized ctx (neither Middleware nor WithLocale ran) it returns the
// key.
func TranslateWith(ctx context.Context, key Key, data Data) string {
	value, ok := ctx.Value(localizedKey{}).(localized)
	if !ok {
		return string(key)
	}
	return value.catalog.Translate(value.locale, key, data)
}

// Middleware negotiates the request locale from Accept-Language, stores
// it in the request context (read it with FromContext, translate with T),
// and sets Content-Language and "Vary: Accept-Language" on the response
// so caches never serve one language's response to another.
func (catalog *Catalog) Middleware(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		locale := catalog.Negotiate(r.Header.Get(AcceptLanguageHeader))
		header := w.Header()
		header.Set(ContentLanguageHeader, locale.String())
		header.Add(varyHeader, AcceptLanguageHeader)
		next.ServeHTTP(w, r.WithContext(catalog.WithLocale(r.Context(), locale)))
	})
}
