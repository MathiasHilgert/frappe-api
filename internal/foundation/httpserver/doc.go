// Package httpserver is the foundation layer for the application's HTTP
// server: a hardened stdlib http.Server exposing liveness and readiness
// probes outside the API surface, and a versioned Huma v2 API ("/v1")
// wrapped with tracing, panic recovery, request id and access log
// middleware.
//
// # Route ownership
//
// Neither this package nor internal/dependencies (the composition root)
// ever declares, lists or knows a single module route. This package only
// builds the "/v1" huma.API and hands it out through Server.V1; the
// composition root only receives that huma.API and passes it, unchanged,
// into a module's constructor. Registering operations on it is entirely
// the module's own responsibility, done in that module's own
// adapters/http subpackage and invoked from the module's own
// constructor or Register method. This keeps every module's HTTP surface
// self-contained: adding, renaming or removing a module's endpoints never
// touches httpserver or dependencies.
//
// # Module integration
//
// A module that wants HTTP endpoints declares an adapters/http
// subpackage whose job is to register that module's own operations on
// the huma.API it is given:
//
//	// internal/modules/orders/adapters/http/http.go
//	package http
//
//	import (
//		"github.com/danielgtaylor/huma/v2"
//
//		"github.com/MathiasHilgert/frappe-api/internal/modules/orders/application"
//	)
//
//	// Register adds the orders module's own operations onto api. api is
//	// the shared "/v1" group; every path given here is relative to it, so
//	// registering "/orders" here serves "/v1/orders". This package is the
//	// only place that knows these paths.
//	func Register(api huma.API, service *application.Service) {
//		huma.Get(api, "/orders", listOrders(service))
//		huma.Post(api, "/orders", createOrder(service))
//	}
//
// The module's own constructor (or Register method, if it implements
// internal/foundation/application.Module) calls this adapters/http
// Register function with the huma.API it received:
//
//	// internal/modules/orders/module.go
//	package orders
//
//	import "github.com/danielgtaylor/huma/v2"
//
//	import httpadapter "github.com/MathiasHilgert/frappe-api/internal/modules/orders/adapters/http"
//
//	func New(api huma.API, dependencies Dependencies) *Module {
//		service := application.NewService(dependencies)
//		httpadapter.Register(api, service)
//		return &Module{service: service}
//	}
//
// # Composition root wiring
//
// The composition root (internal/dependencies) builds the Server once,
// hands api := server.V1() to each module's constructor as it wires that
// module in, and separately registers the Server itself as an
// application.Dependency[*Server] so its Listen and Shutdown run in the
// application lifecycle:
//
//	server := httpserver.New(httpserver.Settings{ /* ... */ })
//
//	ordersModule := orders.New(server.V1(), ordersDependencies)
//	instance.Use(ordersModule)
//
//	application.Provide(instance, application.Dependency[*httpserver.Server]{
//		Name: httpserver.DependencyName,
//		Up: func(context.Context) (*httpserver.Server, error) {
//			return server, server.Listen()
//		},
//		Down: func(ctx context.Context, server *httpserver.Server) error {
//			return server.Shutdown(ctx)
//		},
//	})
//
// dependencies.go never imports huma.Register or names a single module
// path; it only threads the huma.API through. See
// internal/dependencies/doc.go for the composition root's own
// conventions.
//
// # Architecture
//
// This package, and each module's own adapters/http subpackage (and
// module root, for wiring), are the only places allowed to import
// github.com/danielgtaylor/huma/v2: .golangci.yml's depguard rules deny
// it from domain and application layers, keeping business logic
// transport-agnostic. net/http itself is denied from domain and
// application for the same reason.
package httpserver
