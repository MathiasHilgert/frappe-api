package dependencies_test

import (
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
)

// repositoryRoot locates the repository root from this test file's own
// path, so the test works regardless of the working directory go test is
// invoked from.
func repositoryRoot(t *testing.T) string {
	t.Helper()

	_, thisFile, _, ok := runtime.Caller(0)
	if !ok {
		t.Fatal("runtime.Caller failed to resolve this test file's path")
	}
	// This file lives at internal/dependencies/architecture_boundaries_test.go.
	return filepath.Join(filepath.Dir(thisFile), "..", "..")
}

// TestApplicationLayerCannotDependOnDatabase pins down, as an executable
// regression test, the two configuration files that keep the RLS/
// transaction helper in internal/foundation/database (and pgx in general)
// out of reach of module application layers (commands/queries). Only
// module adapters, module root and the composition root
// (internal/dependencies) may import it.
//
// This is a static assertion over the enforcing config, not a run of the
// linters themselves (those require network access to fetch
// go-arch-lint and are exercised by "task architecture:check" / CI
// instead); it fails loudly if either rule is ever weakened or removed.
func TestApplicationLayerCannotDependOnDatabase(t *testing.T) {
	root := repositoryRoot(t)

	depguard, err := os.ReadFile(filepath.Join(root, ".golangci.yml"))
	if err != nil {
		t.Fatalf("read .golangci.yml: %v", err)
	}
	if !strings.Contains(string(depguard), "github.com/MathiasHilgert/frappe-api/internal/foundation/database") {
		t.Fatal(".golangci.yml no longer denies internal/foundation/database for the application layer")
	}
	if !strings.Contains(string(depguard), "github.com/jackc") {
		t.Fatal(".golangci.yml no longer denies the pgx driver for the domain/application layers")
	}

	archLint, err := os.ReadFile(filepath.Join(root, ".go-arch-lint.yml"))
	if err != nil {
		t.Fatalf("read .go-arch-lint.yml: %v", err)
	}
	moduleApplicationSection := sectionAfter(string(archLint), "module_application:\n    mayDependOn:")
	if strings.Contains(moduleApplicationSection, "- foundation") {
		t.Fatal(".go-arch-lint.yml now lets module_application depend on foundation, which contains internal/foundation/database")
	}
}

// sectionAfter returns the text of source starting at marker up to (but
// excluding) the next top-level (two-space-indented) mapping key, which is
// enough to isolate one component's mayDependOn list in .go-arch-lint.yml's
// flat indentation style.
func sectionAfter(source, marker string) string {
	index := strings.Index(source, marker)
	if index == -1 {
		return ""
	}
	rest := source[index+len(marker):]
	lines := strings.Split(rest, "\n")

	var section strings.Builder
	for _, line := range lines {
		trimmed := strings.TrimRight(line, " ")
		if trimmed != "" && !strings.HasPrefix(trimmed, "    ") {
			break
		}
		section.WriteString(line)
		section.WriteString("\n")
	}
	return section.String()
}
