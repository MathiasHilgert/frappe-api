package main

import "strings"

// disambiguationBrackets pairs the half-width and full-width parentheses
// (and square brackets) sources use to disambiguate a name
// ("Córdova (província da Argentina)", "コルドバ（アルゼンチン）").
var disambiguationBrackets = [][2]string{{"(", ")"}, {"（", "）"}, {"[", "]"}} //nolint:gochecknoglobals // constant lookup table.

// stripDisambiguation removes every trailing bracketed qualifier from
// name, so a localized name reads like the place's own name. Brackets in
// the middle of a name are kept ("ココス(キーリング)諸島"); an unclosed
// trailing qualifier ("Baucau (municipalité", a truncated source value)
// is removed too.
func stripDisambiguation(name string) string {
	name = strings.TrimSpace(name)
	for {
		stripped := false
		for _, brackets := range disambiguationBrackets {
			if cut, found := trailingQualifierStart(name, brackets); found {
				name = strings.TrimSpace(name[:cut])
				stripped = true
			}
		}
		if !stripped {
			return name
		}
	}
}

// trailingQualifierStart returns where a trailing qualifier in brackets
// starts: a closed one ending name, or an opening bracket never closed.
func trailingQualifierStart(name string, brackets [2]string) (int, bool) {
	open := strings.LastIndex(name, brackets[0])
	if open < 0 {
		return 0, false
	}
	if strings.HasSuffix(name, brackets[1]) || !strings.Contains(name[open:], brackets[1]) {
		return open, true
	}
	return 0, false
}
