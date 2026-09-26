package database

import (
	"strings"
)

// statementNamePrefix starts the comment line that names a statement.
const statementNamePrefix = "-- name:"

// statementName names the span otelpgx creates for each query. A
// statement whose first line is "-- name: <name>" (for example
// "-- name: geo.list_countries") is named <name>, so every repository
// query shows up by its own name; any other statement keeps the
// low-cardinality operation name ("SELECT"), never its text.
type statementName struct{}

func (statementName) of(statement string) string {
	statement = strings.TrimSpace(statement)
	if rest, found := strings.CutPrefix(statement, statementNamePrefix); found {
		line, _, _ := strings.Cut(rest, "\n")
		if name := strings.TrimSpace(line); name != "" {
			return name
		}
	}
	for _, line := range strings.Split(statement, "\n") {
		line = strings.TrimSpace(line)
		if line == "" || strings.HasPrefix(line, "--") {
			continue
		}
		operation, _, _ := strings.Cut(line, " ")
		return strings.ToUpper(operation)
	}
	return "query"
}
