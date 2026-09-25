package i18n

import (
	"errors"
	"fmt"

	"golang.org/x/text/language"
)

// Locale is a validated, canonical BCP 47 language tag, such as es-419,
// pt-BR or zh-Hans. The zero value is not a valid locale; build one with
// ParseLocale.
type Locale struct {
	tag language.Tag
}

// errUndeterminedLocale reports a tag that parses but names no language.
var errUndeterminedLocale = errors.New("locale names no language")

// ParseLocale parses value as a BCP 47 tag and returns it in canonical
// form (pt-br becomes pt-BR). It rejects malformed tags and "und".
func ParseLocale(value string) (Locale, error) {
	tag, err := language.Parse(value)
	if err != nil {
		return Locale{}, fmt.Errorf("parse locale %q: %w", value, err)
	}
	if tag == language.Und {
		return Locale{}, fmt.Errorf("parse locale %q: %w", value, errUndeterminedLocale)
	}
	return Locale{tag: tag}, nil
}

// MustParseLocale is ParseLocale for constants known to be valid; it
// panics on an invalid value.
func MustParseLocale(value string) Locale {
	locale, err := ParseLocale(value)
	if err != nil {
		panic(err)
	}
	return locale
}

// ParseLocales parses every value with ParseLocale, keeping their order,
// and rejects duplicates (compared in canonical form).
func ParseLocales(values []string) ([]Locale, error) {
	locales := make([]Locale, 0, len(values))
	seen := make(map[language.Tag]bool, len(values))
	for _, value := range values {
		locale, err := ParseLocale(value)
		if err != nil {
			return nil, err
		}
		if seen[locale.tag] {
			return nil, fmt.Errorf("duplicate locale %q", locale)
		}
		seen[locale.tag] = true
		locales = append(locales, locale)
	}
	return locales, nil
}

// String returns the canonical BCP 47 form, for example "pt-BR".
func (locale Locale) String() string {
	return locale.tag.String()
}

// Tag returns the underlying golang.org/x/text/language tag.
func (locale Locale) Tag() language.Tag {
	return locale.tag
}

// IsZero reports whether locale is the zero value.
func (locale Locale) IsZero() bool {
	return locale.tag == language.Und
}
