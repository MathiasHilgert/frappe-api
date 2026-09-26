// Package architecture_test holds repository-wide code style checks that
// go-arch-lint and golangci-lint cannot express.
package architecture_test

import (
	"go/ast"
	"go/parser"
	"go/token"
	"io/fs"
	"path/filepath"
	"regexp"
	"strings"
	"testing"
)

// methodsOnlyRoots are the directories (relative to the repository root)
// where every function must be a method. The rule is being adopted package
// by package: add a directory here once it complies.
var methodsOnlyRoots = []string{
	"internal/modules/geo",
	"internal/foundation/usecase",
}

// allowedFreeFunction matches the only package-level functions the rule
// allows: constructors, test entry points, main and init.
var allowedFreeFunction = regexp.MustCompile(`^(New|new)([A-Z0-9]|$)|^(Test|Benchmark|Fuzz|Example)|^(main|init)$`)

// methodsOnlyScanner walks the Go files of one root and reports every
// function that is neither a method nor allowed, including function
// literals bound to package-level variables (a free function in disguise).
type methodsOnlyScanner struct {
	t     *testing.T
	files *token.FileSet
}

func (scanner methodsOnlyScanner) scan(root string) (scanned int) {
	scanner.t.Helper()
	err := filepath.WalkDir(root, func(path string, entry fs.DirEntry, err error) error {
		if err != nil || entry.IsDir() || !strings.HasSuffix(path, ".go") {
			return err
		}
		scanned++
		scanner.check(path)
		return nil
	})
	if err != nil {
		scanner.t.Fatalf("walk %s: %v", root, err)
	}
	return scanned
}

func (scanner methodsOnlyScanner) check(path string) {
	scanner.t.Helper()
	file, err := parser.ParseFile(scanner.files, path, nil, parser.SkipObjectResolution)
	if err != nil {
		scanner.t.Fatalf("parse %s: %v", path, err)
	}
	for _, declaration := range file.Decls {
		switch declaration := declaration.(type) {
		case *ast.FuncDecl:
			if declaration.Recv == nil && !allowedFreeFunction.MatchString(declaration.Name.Name) {
				scanner.t.Errorf("%s: free function %s: make it a method (only New... constructors, Test/Benchmark/Fuzz/Example, main and init may be free)",
					scanner.files.Position(declaration.Pos()), declaration.Name.Name)
			}
		case *ast.GenDecl:
			scanner.checkVariables(declaration)
		}
	}
}

func (scanner methodsOnlyScanner) checkVariables(declaration *ast.GenDecl) {
	scanner.t.Helper()
	for _, specification := range declaration.Specs {
		values, ok := specification.(*ast.ValueSpec)
		if !ok {
			continue
		}
		for index, value := range values.Values {
			if _, literal := value.(*ast.FuncLit); literal {
				scanner.t.Errorf("%s: package-level function literal %s: make it a method",
					scanner.files.Position(value.Pos()), values.Names[index].Name)
			}
		}
	}
}

func TestMethodsOnly(t *testing.T) {
	t.Parallel()
	for _, root := range methodsOnlyRoots {
		scanned := methodsOnlyScanner{t: t, files: token.NewFileSet()}.scan(filepath.Join("..", "..", filepath.FromSlash(root)))
		if scanned == 0 {
			t.Errorf("%s has no Go files: remove it from methodsOnlyRoots or fix the path", root)
		}
	}
}

func TestMethodsOnlyRejectsFreeFunctions(t *testing.T) {
	t.Parallel()
	source := `package sample
func NewThing() {}
func newThing() {}
func TestThing() {}
func init() {}
type thing struct{}
func (thing) helper() {}
func helper() {}
var hidden = func() {}
`
	files := token.NewFileSet()
	file, err := parser.ParseFile(files, "sample.go", source, 0)
	if err != nil {
		t.Fatalf("parse: %v", err)
	}
	var offenders []string
	for _, declaration := range file.Decls {
		if function, ok := declaration.(*ast.FuncDecl); ok && function.Recv == nil && !allowedFreeFunction.MatchString(function.Name.Name) {
			offenders = append(offenders, function.Name.Name)
		}
	}
	if strings.Join(offenders, ",") != "helper" {
		t.Errorf("offenders = %v, want only helper", offenders)
	}
}
