// Package i18n is the platform's internationalization foundation: the
// Locale model (canonical BCP 47 tags), Accept-Language negotiation and
// the static message catalogs embedded in the binary.
//
// # Locales
//
// The platform serves the locales listed in I18N_SUPPORTED_LOCALES
// (default es-419,en,pt-BR,fr,it,de,ru,zh-Hans,ko,ja). I18N_SOURCE_LOCALE
// (default es-419) is the locale every message is authored in and the
// final fallback.
//
// # Negotiation
//
// Catalog.Middleware, mounted by internal/foundation/httpserver on the /v1
// API, picks the best supported locale for Accept-Language with a
// golang.org/x/text/language Matcher (es-AR matches es-419, pt matches
// pt-BR), falling back to the source locale. It stores the locale in the
// request context and sets Content-Language and "Vary: Accept-Language".
//
// # Static messages
//
// Messages live in locales/<tag>.json (go-i18n JSON: a flat
// "key": "text" object, or {"one": ..., "other": ...} for plurals, with
// text/template placeholders such as {{.Name}}). Catalogs are raw UTF-8
// by design, exempt from the repository's plain-ASCII rule, so
// translators can read and edit them directly. In a handler:
//
//	title := i18n.T(ctx, "business_type.restaurant.name")
//	count := i18n.TranslateWith(ctx, "menu.items", i18n.Data{"Count": n})
//
// A key missing in the requested locale falls back to the source locale,
// then to the key itself. TestEmbeddedCatalogsAreComplete fails when any
// catalog lacks a key present in the source locale, so CI rejects
// incomplete translations.
package i18n
