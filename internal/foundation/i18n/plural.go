package i18n

import (
	"encoding/json"
	"fmt"
	"io/fs"
	"sort"
	"strconv"

	goi18n "github.com/nicksnyder/go-i18n/v2/i18n"
)

// pluralSampleCounts returns counts that together reach every CLDR
// cardinal category of every language: integers 0 to 200, one million
// (the "many" form of es, fr, it and pt) and decimals.
func pluralSampleCounts() []any {
	counts := make([]any, 0, 205)
	for number := range 201 {
		counts = append(counts, number)
	}
	return append(counts, 1000000, "0.5", "1.5", "2.5")
}

// CheckPluralForms reports, for every locale, each plural message of the
// source locale (one that defines any form besides "other") that cannot
// render some count in that locale because it lacks a CLDR plural
// category the locale uses (for example "few" and "many" in ru). It uses
// the same plural rules as Translate, so a clean result means no count
// ever falls back. It backs the catalog completeness test.
func CheckPluralForms(messages fs.FS, source string, locales []string) ([]string, error) {
	sourceMessages, err := parseMessages(messages, source)
	if err != nil {
		return nil, err
	}
	var problems []string
	for _, name := range locales {
		locale, err := ParseLocale(name)
		if err != nil {
			return nil, err
		}
		translated, err := parseMessages(messages, name)
		if err != nil {
			return nil, err
		}
		for id, message := range sourceMessages {
			if isPlural(message) {
				problems = append(problems, pluralProblems(locale, id, translated[id])...)
			}
		}
	}
	sort.Strings(problems)
	return problems, nil
}

// pluralProblems renders message with every sample count in locale alone
// (no fallback) and reports each distinct failure.
func pluralProblems(locale Locale, id string, message *goi18n.Message) []string {
	if message == nil {
		return []string{fmt.Sprintf("%s: %s: missing message", locale, id)}
	}
	bundle := goi18n.NewBundle(locale.tag)
	if err := bundle.AddMessages(locale.tag, message); err != nil {
		return []string{fmt.Sprintf("%s: %s: %v", locale, id, err)}
	}
	localizer := goi18n.NewLocalizer(bundle, locale.String())
	seen := map[string]bool{}
	var problems []string
	for _, count := range pluralSampleCounts() {
		_, err := localizer.Localize(&goi18n.LocalizeConfig{MessageID: id, PluralCount: count, TemplateData: map[string]any{countField: count}})
		if err == nil || seen[err.Error()] {
			continue
		}
		seen[err.Error()] = true
		problems = append(problems, fmt.Sprintf("%s: %s: %v (count %s)", locale, id, err, formatCount(count)))
	}
	return problems
}

func formatCount(count any) string {
	if number, ok := count.(int); ok {
		return strconv.Itoa(number)
	}
	return fmt.Sprint(count)
}

func isPlural(message *goi18n.Message) bool {
	return message.Zero != "" || message.One != "" || message.Two != "" || message.Few != "" || message.Many != ""
}

// parseMessages parses locale's "<tag>.json" file in messages, keyed by
// message ID.
func parseMessages(messages fs.FS, locale string) (map[string]*goi18n.Message, error) {
	path := locale + ".json"
	content, err := fs.ReadFile(messages, path)
	if err != nil {
		return nil, fmt.Errorf("catalog for locale %s: %w", locale, err)
	}
	file, err := goi18n.ParseMessageFileBytes(content, path, map[string]goi18n.UnmarshalFunc{"json": json.Unmarshal})
	if err != nil {
		return nil, fmt.Errorf("catalog for locale %s: %w", locale, err)
	}
	parsed := make(map[string]*goi18n.Message, len(file.Messages))
	for _, message := range file.Messages {
		parsed[message.ID] = message
	}
	return parsed, nil
}
