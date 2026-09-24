// Package dependencies is the composition root of the application: it is
// the only place that builds a foundation/application.Application, loads
// the application-wide foundation/configuration.Configuration, wires
// concrete modules into it, and registers concrete dependencies.
//
// Convention: each dependency gets its own file in this package, named
// after the dependency it builds (for example database.go for a database
// connection pool). Each such file exposes a small function that takes
// whatever configuration it needs and returns an
// application.Dependency[T] ready to be registered with
// application.Provide. There is no dependency to build yet, so this
// package currently only assembles the Application and its modules; the
// per-dependency files will be added alongside the first real module. By
// convention, once a module exists, each dependency file exposes module
// structs it builds from the relevant slice of Configuration.
//
// Modules never see the whole Configuration and never import the
// foundation/configuration package themselves (this is enforced by
// .go-arch-lint.yml and depguard). Instead, each module declares its own
// Configuration and Dependencies structs in its module.go, describing
// exactly what it needs. This package is the only place that knows the
// full Configuration; for each module it registers, it extracts the
// relevant fields from Configuration and passes them into that module's
// constructor. This keeps every module's dependencies explicit and
// testable in isolation, without reaching back into the composition root.
//
// # HTTP routes belong to modules, never to this package
//
// This package builds the foundation/httpserver.Server once and, for
// each module it wires in, hands that module's constructor the shared
// "/v1" huma.API returned by Server.V1. It never calls huma.Register (or
// huma.Get/Post/...) itself and never names a single module's route: it
// only threads the huma.API through, exactly like it threads through a
// database handle. Declaring, listing or knowing routes is entirely the
// owning module's job, done in that module's own adapters/http
// subpackage; see foundation/httpserver's package doc for the full
// module-side convention and a worked example.
package dependencies
