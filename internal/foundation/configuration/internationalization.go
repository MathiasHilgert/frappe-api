package configuration

import "golang.org/x/text/language"

// Internationalization defaults. They must equal the envDefault tags on
// Internationalization (struct tags cannot reference constants; a test
// keeps them in sync), and every default locale ships an embedded catalog.
const (
	DefaultSupportedLocales = "es-419,en,pt-BR,fr,it,de,ru,zh-Hans,ko,ja"
	DefaultSourceLocale     = "es-419"
)

// validateInternationalization checks the I18N_* settings: every
// supported locale must be a well-formed BCP 47 tag, listed once
// (compared in canonical form), and the source locale must be one of
// them.
func validateInternationalization(settings Internationalization) []Violation {
	var violations []Violation
	supported := make(map[language.Tag]bool, len(settings.SupportedLocales))
	valid := len(settings.SupportedLocales) > 0
	for _, value := range settings.SupportedLocales {
		tag, ok := parseLocale(value)
		if !ok || supported[tag] {
			valid = false
			break
		}
		supported[tag] = true
	}
	if !valid {
		violations = append(violations, Violation{Variable: "I18N_SUPPORTED_LOCALES", Rule: "unique_canonical_bcp47_locales"})
	}

	source, ok := parseLocale(settings.SourceLocale)
	if !ok || !supported[source] {
		violations = append(violations, Violation{Variable: "I18N_SOURCE_LOCALE", Rule: "canonical_one_of_supported_locales"})
	}
	return violations
}

// parseLocale parses value as a BCP 47 tag that names a language and is
// already written in canonical form: deprecated codes (iw, in, ji) and
// non-canonical casing (pt-br, zh-hans) are rejected, so the configured
// value is exactly what Content-Language and the catalog file name use.
func parseLocale(value string) (language.Tag, bool) {
	tag, err := language.All.Parse(value)
	if err != nil || tag == language.Und || tag.String() != value {
		return language.Und, false
	}
	return tag, true
}
