package migrations

import "embed"

// FS embeds every migration SQL file so cmd/migrate can apply them without
// depending on a filesystem path at runtime (for example inside a
// container image that ships only the compiled binary).
//
//go:embed *.sql
var FS embed.FS
