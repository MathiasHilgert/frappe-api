package machinetranslation

import (
	"context"
	"errors"
	"fmt"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/localizedtext"
)

// Translator is the machine translation provider port. DeepL is one
// adapter (the deepl subpackage); tests use a fake.
type Translator interface {
	// Translate translates every text of request, keeping order: the
	// result has exactly one translation per text. It must classify
	// provider failures with RateLimitedError, ErrQuotaExceeded and
	// PermanentError; any other error is treated as transient.
	Translate(ctx context.Context, request Request) ([]string, error)
}

// Request is one batch of texts sharing a source locale, a target locale
// and a context.
type Request struct {
	Source i18n.Locale
	Target i18n.Locale
	// Context is a hint that influences the translation without being
	// translated itself, such as "Description of a dish on a restaurant
	// menu". Empty sends none.
	Context string
	Texts   []string
}

// ErrQuotaExceeded reports that the provider's billing quota (or a cost
// control limit) is exhausted: retrying before the period renews only
// wastes attempts, so the job is cancelled.
var ErrQuotaExceeded = errors.New("machinetranslation: provider quota exceeded")

// RateLimitedError reports too many requests; the job is snoozed for
// RetryAfter (a default when the provider sent none).
type RateLimitedError struct {
	RetryAfter time.Duration
}

func (limited RateLimitedError) Error() string {
	return fmt.Sprintf("machinetranslation: provider rate limited the request (retry after %s)", limited.RetryAfter)
}

// PermanentError reports a request the provider will never accept as is
// (bad credentials, unsupported language, payload too large): retrying
// cannot help, so the job is cancelled.
type PermanentError struct {
	Cause error
}

func (permanent PermanentError) Error() string {
	return "machinetranslation: permanent provider failure: " + permanent.Cause.Error()
}

func (permanent PermanentError) Unwrap() error {
	return permanent.Cause
}

// Texts is the subset of *localizedtext.Service the jobs need.
type Texts interface {
	Get(ctx context.Context, id localizedtext.ID) (localizedtext.Text, error)
	SetMachineTranslation(ctx context.Context, id localizedtext.ID, locale i18n.Locale, value, sourceHash string) (bool, error)
	RequestExpired(ctx context.Context, limit int) (int, error)
	DeleteOrphans(ctx context.Context, olderThan time.Duration, limit int) (int64, error)
	Tenants(ctx context.Context) ([]string, error)
}

// Transactor runs work in a database transaction scoped to tenant (the
// Row Level Security setting localizedtext relies on). An empty tenant
// runs without one, which only the tenant listing needs.
type Transactor func(ctx context.Context, tenant string, work func(ctx context.Context) error) error
