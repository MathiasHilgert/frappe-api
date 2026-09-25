package i18n

import (
	"embed"
	"encoding/json"
	"errors"
	"fmt"
	"io/fs"

	goi18n "github.com/nicksnyder/go-i18n/v2/i18n"
	"golang.org/x/text/language"
)

// EmbeddedMessages holds the static catalogs compiled into the binary,
// one locales/<BCP 47 tag>.json file per supported locale.
//
//go:embed locales/*.json
var embeddedLocales embed.FS

// EmbeddedMessages is the locales directory of embeddedLocales, so files
// are addressed as "<tag>.json".
var EmbeddedMessages = mustSub(embeddedLocales, "locales")

func mustSub(fileSystem fs.FS, directory string) fs.FS {
	sub, err := fs.Sub(fileSystem, directory)
	if err != nil {
		panic(err)
	}
	return sub
}

// Key identifies a static message, for example
// "business_type.restaurant.name". Untyped string constants convert to
// it implicitly.
type Key string

// Data holds the template values of a message, for example
// Data{"Name": name}. A "Count" entry also selects the CLDR plural form.
type Data map[string]any

// countField is the Data entry that selects the plural form.
const countField = "Count"

// Settings configures a Catalog.
type Settings struct {
	// Messages holds one "<tag>.json" file per supported locale. Use
	// EmbeddedMessages in production.
	Messages fs.FS
	// Source is the locale every message is authored in and the final
	// fallback. It must be one of Supported.
	Source Locale
	// Supported lists the locales the platform serves, in preference
	// order for ties during negotiation.
	Supported []Locale
}

// Catalog holds the static translations of every supported locale and
// negotiates request locales. It is safe for concurrent use.
type Catalog struct {
	bundle     *goi18n.Bundle
	matcher    language.Matcher
	localizers map[language.Tag]*goi18n.Localizer
	source     Locale
	supported  []Locale
}

var (
	errSourceRequired     = errors.New("source locale is required")
	errSourceNotSupported = errors.New("source locale is not a supported locale")
	errMessagesRequired   = errors.New("messages are required")
)

// NewCatalog loads the catalog of every supported locale from
// settings.Messages. It fails if the source is missing or unsupported, or
// if any supported locale has no catalog file or a malformed one, so a
// misconfiguration stops the application at startup.
func NewCatalog(settings Settings) (*Catalog, error) {
	if settings.Source.IsZero() {
		return nil, errSourceRequired
	}
	if !containsLocale(settings.Supported, settings.Source) {
		return nil, fmt.Errorf("%w: %s", errSourceNotSupported, settings.Source)
	}
	if settings.Messages == nil {
		return nil, errMessagesRequired
	}

	bundle := goi18n.NewBundle(settings.Source.tag)
	bundle.RegisterUnmarshalFunc("json", json.Unmarshal)
	for _, locale := range settings.Supported {
		path := fileName(locale)
		content, err := fs.ReadFile(settings.Messages, path)
		if err != nil {
			return nil, fmt.Errorf("catalog for locale %s: %w", locale, err)
		}
		if _, err := bundle.ParseMessageFileBytes(content, path); err != nil {
			return nil, fmt.Errorf("catalog for locale %s: %w", locale, err)
		}
	}

	// The source goes first so it is the matcher's default for requests
	// that match nothing.
	ordered := make([]Locale, 0, len(settings.Supported))
	ordered = append(ordered, settings.Source)
	for _, locale := range settings.Supported {
		if locale != settings.Source {
			ordered = append(ordered, locale)
		}
	}
	tags := make([]language.Tag, 0, len(ordered))
	localizers := make(map[language.Tag]*goi18n.Localizer, len(ordered))
	for _, locale := range ordered {
		tags = append(tags, locale.tag)
		localizers[locale.tag] = goi18n.NewLocalizer(bundle, locale.String())
	}

	return &Catalog{
		bundle:     bundle,
		matcher:    language.NewMatcher(tags),
		localizers: localizers,
		source:     settings.Source,
		supported:  ordered,
	}, nil
}

// Source returns the source (default) locale.
func (catalog *Catalog) Source() Locale {
	return catalog.source
}

// Supported returns the supported locales, source first.
func (catalog *Catalog) Supported() []Locale {
	return append([]Locale(nil), catalog.supported...)
}

// Negotiate returns the supported locale that best matches an
// Accept-Language header value, or the source locale when the header is
// empty, malformed or matches nothing.
func (catalog *Catalog) Negotiate(acceptLanguage string) Locale {
	if acceptLanguage == "" {
		return catalog.source
	}
	requested, _, err := language.ParseAcceptLanguage(acceptLanguage)
	if err != nil || len(requested) == 0 {
		return catalog.source
	}
	_, index, confidence := catalog.matcher.Match(requested...)
	if confidence == language.No {
		return catalog.source
	}
	return catalog.supported[index]
}

// Translate renders key in locale with data, falling back to the source
// locale when locale lacks the key, and to the key itself when no locale
// has it (so a missing message is visible, never a failed request). An
// unsupported locale is served as the source.
func (catalog *Catalog) Translate(locale Locale, key Key, data Data) string {
	configuration := &goi18n.LocalizeConfig{MessageID: string(key), TemplateData: map[string]any(data)}
	if count, ok := data[countField]; ok {
		configuration.PluralCount = count
	}
	// Fallback chain: requested locale, then source, then the key.
	for _, tag := range []language.Tag{locale.tag, catalog.source.tag} {
		localizer, ok := catalog.localizers[tag]
		if !ok {
			continue
		}
		if message, err := localizer.Localize(configuration); err == nil && message != "" {
			return message
		}
	}
	return string(key)
}

// MessageKeys returns the messages of locale's "<tag>.json" file in
// messages, keyed by message ID, with each message's "other" form as the
// value. It backs the catalog completeness test.
func MessageKeys(messages fs.FS, locale string) (map[string]string, error) {
	path := locale + ".json"
	content, err := fs.ReadFile(messages, path)
	if err != nil {
		return nil, fmt.Errorf("catalog for locale %s: %w", locale, err)
	}
	file, err := goi18n.ParseMessageFileBytes(content, path, map[string]goi18n.UnmarshalFunc{"json": json.Unmarshal})
	if err != nil {
		return nil, fmt.Errorf("catalog for locale %s: %w", locale, err)
	}
	keys := make(map[string]string, len(file.Messages))
	for _, message := range file.Messages {
		keys[message.ID] = message.Other
	}
	return keys, nil
}

func fileName(locale Locale) string {
	return locale.String() + ".json"
}

func containsLocale(locales []Locale, wanted Locale) bool {
	for _, locale := range locales {
		if locale == wanted {
			return true
		}
	}
	return false
}
