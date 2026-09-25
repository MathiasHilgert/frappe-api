// Package build holds build-time identity for the running binary. Both
// variables are meant to be overridden at compile time with
// -ldflags "-X ...", typically from the Dockerfile's build stage; their
// zero values are placeholders for local, unstamped builds.
package build

// Version is the build's version string, normally a Git tag or a
// semantic version. It is overridden at build time with
// -ldflags "-X github.com/MathiasHilgert/frappe-api/internal/foundation/build.Version=...".
var Version = "development"

// Commit is the build's source commit hash. It is overridden at build
// time with
// -ldflags "-X github.com/MathiasHilgert/frappe-api/internal/foundation/build.Commit=...".
var Commit = "unknown"
