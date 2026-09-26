package database

import "testing"

func TestStatementNameUsesTheDeclaredNameElseTheOperation(t *testing.T) {
	t.Parallel()
	cases := map[string]string{
		"-- name: geo.list_countries\nSELECT code FROM countries":     "geo.list_countries",
		"\n\t-- name:   geo.search_places  \nWITH hits AS (SELECT 1)": "geo.search_places",
		"select 1":                               "SELECT",
		"  INSERT INTO t VALUES (1)":             "INSERT",
		"-- a plain comment\nUPDATE t SET a = 1": "UPDATE",
		"":                                       "query",
	}
	for statement, want := range cases {
		if got := (statementName{}).of(statement); got != want {
			t.Errorf("of(%q) = %q, want %q", statement, got, want)
		}
	}
}
