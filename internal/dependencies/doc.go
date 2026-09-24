// Package dependencies is the composition root of the application: it is
// the only place that builds a foundation/application.Application, wires
// concrete modules into it, and registers concrete dependencies.
//
// Convention: each dependency gets its own file in this package, named
// after the dependency it builds (for example database.go for a database
// connection pool). Each such file exposes a small function that takes
// whatever configuration it needs and returns an
// application.Dependency[T] ready to be registered with
// application.Provide. There is no dependency to build yet, so this
// package currently only assembles the Application and its modules; the
// per-dependency files will be added alongside the first real module.
package dependencies
