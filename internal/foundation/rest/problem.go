package rest

import (
	"context"

	"github.com/danielgtaylor/huma/v2"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
)

// Text is a user facing problem text: a catalog key (with its template
// data) resolved in the request's negotiated locale.
type Text struct {
	// Data fills the message template, for example {"ID": "AR"}.
	Data i18n.Data
	// Key is the message key in internal/foundation/i18n/locales.
	Key i18n.Key
	// Default is used when ctx carries no locale (code running outside
	// the /v1 localization middleware); empty means the key itself.
	Default string
}

// In renders text in the locale ctx carries (falling back to the source
// locale, then the key), or Default without a locale.
func (text Text) In(ctx context.Context) string {
	if _, localized := i18n.FromContext(ctx); !localized {
		if text.Default != "" {
			return text.Default
		}
		return string(text.Key)
	}
	return i18n.TranslateWith(ctx, text.Key, text.Data)
}

// Problem returns an RFC 9457 problem with status whose detail is detail
// rendered in the request locale. Title stays the status reason.
func Problem(ctx context.Context, status int, detail Text, details ...*huma.ErrorDetail) huma.StatusError {
	errs := make([]error, 0, len(details))
	for _, entry := range details {
		errs = append(errs, entry)
	}
	return huma.NewError(status, detail.In(ctx), errs...)
}

// Detail returns an errors[] entry at location whose message is message
// rendered in the request locale. value is the rejected value, or nil
// when it must not be echoed.
func Detail(ctx context.Context, location string, message Text, value any) *huma.ErrorDetail {
	return &huma.ErrorDetail{Location: location, Message: message.In(ctx), Value: value}
}
