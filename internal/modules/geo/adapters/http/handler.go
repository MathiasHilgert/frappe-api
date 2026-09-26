// Package http registers the geo module's read-only public API on the
// shared "/v1" huma.API, one handler per resource. Handlers depend on the
// use cases through usecase.QueryHandler, never on concrete types, so they
// are unit tested with fakes.
//
// Countries are addressed by ISO 3166-1 alpha-2 code, subdivisions and
// cities by GeoNames id, time zones by IANA id (slashes included). Names
// are in the negotiated language (Accept-Language), falling back to each
// place's own name. Every response is public reference data: it is
// cacheable by shared caches (Public) with an ETag derived from the geo
// data revision and the response language, so If-None-Match revalidates
// to 304 until a migration changes the data.
package http

import (
	"context"
	"errors"
	"net/http"
	"time"

	"github.com/danielgtaylor/huma/v2"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/httpserver"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/rest"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/usecase"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application/query"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// Cache lifetimes of every geo response: reference data changes only
// with a deploy, and the ETag revalidates it cheaply after that.
const (
	cacheMaxAge               = time.Hour
	cacheStaleWhileRevalidate = 24 * time.Hour
)

// tag groups the operations in the OpenAPI document.
const tag = "Geo"

// Shared is what every geo resource handler needs.
type Shared struct {
	// Revision returns the geo data revision the ETags derive from.
	Revision usecase.QueryHandler[query.GetDataRevision, string]
	// Cursors signs and verifies pagination cursors.
	Cursors *rest.CursorCodec
}

// handler holds the behavior every resource handler shares.
type handler struct {
	shared Shared
}

// operation describes one GET operation.
func (handler) operation(identifier, path, summary, description string) huma.Operation {
	return huma.Operation{
		OperationID: identifier,
		Method:      http.MethodGet,
		Path:        path,
		Summary:     summary,
		Description: description,
		Tags:        []string{tag},
	}
}

// locale is the locale the localization middleware negotiated, or the
// zero locale (every name falls back to the place's own) without one.
func (handler) locale(ctx context.Context) i18n.Locale {
	locale, _ := i18n.FromContext(ctx)
	return locale
}

// revalidate declares the cache policy and ETag of the response: the geo
// data revision in the response language, so the same URL and language
// produce the same body until a migration changes the data. It returns a
// 304 error when the request's If-None-Match already matches, a problem
// when the revision cannot be read, else nil.
func (handler handler) revalidate(ctx context.Context, locale i18n.Locale) error {
	revision, err := handler.shared.Revision.Handle(ctx, query.GetDataRevision{})
	if err != nil {
		return handler.failure(ctx, err)
	}
	etag := httpserver.ETagFromBody([]byte("geo|" + revision + "|" + locale.String()))
	if httpserver.NotModified(ctx, etag, httpserver.Public(cacheMaxAge, cacheStaleWhileRevalidate)) {
		return huma.Status304NotModified()
	}
	return nil
}

// notFound is the 404 for a resource path id that does not exist, or
// cannot exist because it is malformed; key names the resource's message.
func (handler) notFound(ctx context.Context, key i18n.Key, defaultText, id string) error {
	return rest.Problem(ctx, http.StatusNotFound, rest.Text{Key: key, Data: i18n.Data{"ID": id}, Default: defaultText + " " + id + "."})
}

// lookupFailure maps the error of a lookup by path id: not found is 404.
func (handler handler) lookupFailure(ctx context.Context, err error, key i18n.Key, defaultText, id string) error {
	if errors.Is(err, domain.ErrNotFound) {
		return handler.notFound(ctx, key, defaultText, id)
	}
	return handler.failure(ctx, err)
}

// failure maps a use case error to a problem. Anything unexpected becomes
// a 500 without its cause (Huma would otherwise echo the error text); the
// observed use case already logged and traced it.
func (handler) failure(ctx context.Context, err error) error {
	var statusError huma.StatusError
	if errors.As(err, &statusError) {
		return err
	}
	return rest.Problem(ctx, http.StatusInternalServerError, rest.Text{Key: "problem.internal.detail", Default: "An unexpected error occurred."})
}
