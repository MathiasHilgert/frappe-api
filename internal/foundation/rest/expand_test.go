package rest_test

import (
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/rest"
)

func TestExpansionsParseAcceptsAllowedPaths(t *testing.T) {
	expansions := rest.NewExpansions("country", "region.country")
	expand, err := expansions.Parse([]string{"country", "region.country", "country"})
	if err != nil {
		t.Fatalf("Parse error = %v", err)
	}
	if !expand.Has("country") || !expand.Has("region.country") || expand.Has("region") {
		t.Fatalf("unexpected expansion set %v", expand.Paths())
	}
	if got := expand.Paths(); len(got) != 2 || got[0] != "country" || got[1] != "region.country" {
		t.Fatalf("Paths() = %v, want sorted and deduplicated", got)
	}
}

func TestExpansionsParseRejectsInvalidPaths(t *testing.T) {
	expansions := rest.NewExpansions("country", "region.country")
	cases := map[string][]string{
		"not allowed":  {"owner"},
		"bad syntax":   {"Country"},
		"empty":        {""},
		"trailing dot": {"region."},
		"too deep":     {"a.b.c.d.e"},
		"too many":     make([]string, rest.MaximumExpansions+1),
	}
	for name, values := range cases {
		t.Run(name, func(t *testing.T) {
			if _, err := expansions.Parse(values); err == nil {
				t.Fatalf("Parse(%v) accepted invalid input", values)
			}
		})
	}
}

func TestNewExpansionsPanicsOnInvalidAllowlist(t *testing.T) {
	defer func() {
		if recover() == nil {
			t.Fatal("NewExpansions accepted a path deeper than the maximum depth")
		}
	}()
	rest.NewExpansions("a.b.c.d.e")
}
