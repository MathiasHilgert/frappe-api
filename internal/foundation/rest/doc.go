// Package rest holds the reusable building blocks of the project-wide API
// conventions for module HTTP adapters:
//
//   - List, NewList and ListOutput: the {"object":"list"} collection
//     envelope, with an RFC 8288 Link rel="next" header.
//   - CursorCodec: opaque, HMAC-SHA256 signed (with secret rotation),
//     versioned and scoped pagination cursors.
//   - PageParameters and NewPage: the limit (1 to 100, default 10) and
//     cursor query parameters, and the page built from LIMIT n+1 rows. The
//     cursor scope is derived from the request (principal slot, escaped
//     path, every filter and order_by), never built by hand.
//   - ExpandParameters, Expansions and Expand: expand[] parsing against a
//     per-operation allowlist, at most 4 levels deep and 20 values.
//   - Expandable and NullableExpandable: a related resource property that
//     is the related id, or the resource itself when expanded, declared
//     in OpenAPI as oneOf the id string and the resource (plus null).
//   - CheckNaming: fails when a registered schema property or path segment
//     or query/path parameter is not snake_case (expand[] excepted); the
//     composition root runs it at startup.
//
// Prefixed public identifiers live in internal/foundation/identifier,
// separate from this package, because use cases (the application layer)
// create them and the application layer must not import Huma.
//
// # Why a separate package
//
// httpserver owns the server, its middleware and the Huma API instance;
// modules import it only for cache policies. The conventions are about
// the shape of resources, which every module adapter touches on every
// operation, so they get a small package with no server state, testable
// with humatest alone.
//
// # A paginated operation
//
//	type ListCitiesInput struct {
//		Country string `query:"country" pattern:"^[A-Z]{2}$"`
//		rest.ExpandParameters
//		rest.PageParameters
//	}
//
//	var cityExpansions = rest.NewExpansions("country", "subdivision")
//
//	huma.Get(api, "/geo/cities", func(ctx context.Context, input *ListCitiesInput) (*rest.ListOutput[City], error) {
//		expand, err := cityExpansions.Parse(ctx, input.Expand)
//		if err != nil {
//			return nil, err
//		}
//		var after CityPosition
//		if _, err := input.Position(ctx, codec, &after); err != nil {
//			return nil, err
//		}
//		rows, err := service.Cities(ctx, input.Country, after, input.Limit+1, expand)
//		if err != nil {
//			return nil, err
//		}
//		return rest.NewPage(codec, input.PageParameters, rows,
//			func(last City) any { return CityPosition{Name: last.Name, ID: last.ID} })
//	})
package rest
