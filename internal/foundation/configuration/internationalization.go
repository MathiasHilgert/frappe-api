package configuration

import "golang.org/x/text/language"

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
		violations = append(violations, Violation{Variable: "I18N_SUPPORTED_LOCALES", Rule: "unique_bcp47_locales"})
	}

	source, ok := parseLocale(settings.SourceLocale)
	if !ok || !supported[source] {
		violations = append(violations, Violation{Variable: "I18N_SOURCE_LOCALE", Rule: "one_of_supported_locales"})
	}
	return violations
}

// parseLocale parses value as a BCP 47 tag that names a language.
func parseLocale(value string) (language.Tag, bool) {
	tag, err := language.Parse(value)
	if err != nil || tag == language.Und {
		return language.Und, false
	}
	return tag, true
}
