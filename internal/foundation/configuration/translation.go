package configuration

import (
	"net/url"
	"slices"
	"time"
)

// maximumDeepLBatchSize bounds texts per DeepL request. DeepL limits a
// request to 128 KiB; 50 short menu texts stay far below it.
const maximumDeepLBatchSize = 50

// DeepL holds the machine translation provider settings (see
// internal/foundation/i18n/machinetranslation/deepl). Without an API key
// machine translation is disabled: nothing is requested and reads fall
// back to the source. The remaining fields are only validated with a key.
type DeepL struct {
	// APIKey authenticates with DeepL. Optional; a secret.
	APIKey string `env:"API_KEY"`
	// BaseURL is the API endpoint. Empty picks it from the key: keys
	// ending in ":fx" use https://api-free.deepl.com, others
	// https://api.deepl.com.
	BaseURL string `env:"BASE_URL"`
	// EnglishVariant is the DeepL target for en: EN-US or EN-GB.
	EnglishVariant string `env:"ENGLISH_VARIANT" envDefault:"EN-US"`
	// Formality is empty, prefer_more or prefer_less.
	Formality string `env:"FORMALITY"`
	// Timeout bounds one DeepL request.
	Timeout time.Duration `env:"TIMEOUT" envDefault:"30s"`
	// BatchSize is the maximum number of texts per job and request.
	BatchSize int `env:"BATCH_SIZE" envDefault:"50"`
	// QuotaPause is how long all machine translation pauses after DeepL
	// reported an exhausted quota (456) or rejected the key (401, 403).
	QuotaPause time.Duration `env:"QUOTA_PAUSE" envDefault:"1h"`
}

// LocalizedTexts holds the periodic localized text sweeps. Zero values
// fall back to the machinetranslation defaults.
type LocalizedTexts struct {
	// ExpiredSweepInterval is how often lost machine translation requests
	// are requested again.
	ExpiredSweepInterval time.Duration `env:"EXPIRED_SWEEP_INTERVAL" envDefault:"5m" validate:"min=0"`
	// OrphanSweepInterval is how often unreferenced texts are deleted.
	OrphanSweepInterval time.Duration `env:"ORPHAN_SWEEP_INTERVAL" envDefault:"1h" validate:"min=0"`
	// OrphanMinimumAge is how long an unreferenced text is kept, leaving
	// room for transactions that have not committed its reference yet.
	OrphanMinimumAge time.Duration `env:"ORPHAN_MINIMUM_AGE" envDefault:"24h" validate:"min=0"`
	// SweepLimit bounds the rows one sweep handles per tenant.
	SweepLimit int `env:"SWEEP_LIMIT" envDefault:"500" validate:"min=0"`
	// PendingTimeout is how long a requested machine translation may stay
	// pending (unleased) before reads and the expired sweep request it
	// again.
	PendingTimeout time.Duration `env:"PENDING_TIMEOUT" envDefault:"15m" validate:"min=0"`
	// MaxRequestAttempts is how many requests a machine translation gets
	// before the expired sweep marks it failed.
	MaxRequestAttempts int `env:"MAX_REQUEST_ATTEMPTS" envDefault:"5" validate:"min=0"`
}

// validateDeepL checks the DEEPL_* settings while an API key is set. It
// never reports the key itself.
func validateDeepL(deepL DeepL) []Violation {
	if deepL.APIKey == "" {
		return nil
	}
	var violations []Violation
	if deepL.BaseURL != "" && !absoluteHTTPURL(deepL.BaseURL) {
		violations = append(violations, Violation{Variable: "DEEPL_BASE_URL", Rule: "absolute_http_url"})
	}
	if !slices.Contains([]string{"EN-US", "EN-GB"}, deepL.EnglishVariant) {
		violations = append(violations, Violation{Variable: "DEEPL_ENGLISH_VARIANT", Rule: "oneof=EN-US EN-GB"})
	}
	if !slices.Contains([]string{"", "prefer_more", "prefer_less"}, deepL.Formality) {
		violations = append(violations, Violation{Variable: "DEEPL_FORMALITY", Rule: "oneof=prefer_more prefer_less"})
	}
	for variable, duration := range map[string]time.Duration{"DEEPL_TIMEOUT": deepL.Timeout, "DEEPL_QUOTA_PAUSE": deepL.QuotaPause} {
		if duration <= 0 {
			violations = append(violations, Violation{Variable: variable, Rule: "gt"})
		}
	}
	if deepL.BatchSize < 1 || deepL.BatchSize > maximumDeepLBatchSize {
		violations = append(violations, Violation{Variable: "DEEPL_BATCH_SIZE", Rule: "between_1_and_50"})
	}
	return violations
}

// absoluteHTTPURL reports whether value is an absolute http or https URL.
func absoluteHTTPURL(value string) bool {
	parsed, err := url.Parse(value)
	return err == nil && (parsed.Scheme == "https" || parsed.Scheme == "http") && parsed.Host != ""
}
