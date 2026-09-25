// Package deepl is the DeepL adapter of machinetranslation.Translator,
// on the DeepL API v2 text translation endpoint (POST /v2/translate,
// https://developers.deepl.com/api-reference/translate).
//
//   - Authentication is the "Authorization: DeepL-Auth-Key <key>" header.
//     The key never appears in errors, logs or span attributes.
//   - Free keys (ending in ":fx") use https://api-free.deepl.com, Pro keys
//     https://api.deepl.com (DefaultBaseURL).
//   - One request carries a batch of texts for one source and one target
//     language, plus an optional context, which DeepL does not translate
//     and does not bill.
//   - Target codes come from an allowlist: en is EN-GB or EN-US when the
//     tag names the region, else the configured variant; pt is PT-BR or
//     PT-PT; es is ES-419 or ES; zh is ZH-HANT for Traditional Chinese,
//     else ZH-HANS; everything else is the upper-cased language (fr-CA is
//     FR). Source codes are the language only (ES, EN, PT, ZH).
//   - 429 (and 529) are rate limits, honoring Retry-After; 456 is an
//     exhausted quota; 401 and 403 are rejected credentials; 413 is a
//     payload too large; other 4xx are permanent; 5xx and network errors
//     are transient (https://developers.deepl.com/docs/best-practices/error-handling).
package deepl

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"slices"
	"strconv"
	"strings"
	"time"
	"unicode/utf8"

	"go.opentelemetry.io/contrib/instrumentation/net/http/otelhttp"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/metric"
	"golang.org/x/text/language"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/machinetranslation"
)

const (
	// FreeBaseURL is the DeepL API Free endpoint.
	FreeBaseURL = "https://api-free.deepl.com"
	// ProBaseURL is the DeepL API Pro endpoint.
	ProBaseURL = "https://api.deepl.com"
	// DefaultRetryAfter is how long a rate limited request waits when
	// DeepL sends no Retry-After header.
	DefaultRetryAfter = time.Minute
	// DefaultTimeout bounds one request when Settings.Timeout is zero.
	DefaultTimeout = 30 * time.Second
	// statusQuotaExceeded is DeepL's "quota exceeded" status.
	statusQuotaExceeded = 456
	// statusOverloaded is DeepL's "too many requests, service overloaded".
	statusOverloaded = 529
	// maximumErrorBody bounds how much of an error response is read.
	maximumErrorBody = 1 << 10
	// maximumResponseBody bounds a success response (the request limit is
	// 128 KiB, so the translations stay well below this).
	maximumResponseBody = 4 << 20
	instrumentationName = "github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/machinetranslation/deepl"
)

// absoluteHTTPURL reports whether value is an absolute http or https URL.
func absoluteHTTPURL(value string) bool {
	parsed, err := url.Parse(value)
	return err == nil && (parsed.Scheme == "https" || parsed.Scheme == "http") && parsed.Host != ""
}

// DefaultBaseURL returns the endpoint for apiKey: DeepL API Free keys end
// in ":fx".
func DefaultBaseURL(apiKey string) string {
	if strings.HasSuffix(apiKey, ":fx") {
		return FreeBaseURL
	}
	return ProBaseURL
}

// Settings configures a Client.
type Settings struct {
	// HTTPClient sends requests; nil means a new client with Timeout,
	// instrumented with otelhttp.
	HTTPClient *http.Client
	// APIKey authenticates. Required; a secret.
	APIKey string
	// BaseURL is the API endpoint; empty means DefaultBaseURL(APIKey).
	BaseURL string
	// EnglishVariant is the target code for en: EN-US or EN-GB (DeepL
	// deprecated the unspecified EN target).
	EnglishVariant string
	// Formality is "", prefer_more or prefer_less. Only the prefer_
	// options are accepted: they fall back to the default formality for
	// languages without it instead of failing the request.
	Formality string
	// Timeout bounds one request; zero means DefaultTimeout.
	Timeout time.Duration
}

// Client is the DeepL machinetranslation.Translator.
type Client struct {
	http           *http.Client
	characters     metric.Int64Counter
	billed         metric.Int64Counter
	requests       metric.Int64Counter
	duration       metric.Float64Histogram
	endpoint       string
	authorization  string
	englishVariant string
	formality      string
}

var _ machinetranslation.Translator = (*Client)(nil)

// New validates settings and returns a Client.
func New(settings Settings) (*Client, error) {
	if err := validate(&settings); err != nil {
		return nil, err
	}
	client := &Client{
		http:           settings.HTTPClient,
		endpoint:       strings.TrimRight(settings.BaseURL, "/") + "/v2/translate",
		authorization:  "DeepL-Auth-Key " + settings.APIKey,
		englishVariant: settings.EnglishVariant,
		formality:      settings.Formality,
	}
	if client.http == nil {
		client.http = &http.Client{Timeout: settings.Timeout, Transport: otelhttp.NewTransport(http.DefaultTransport)}
	}
	if err := client.instrument(); err != nil {
		return nil, err
	}
	return client, nil
}

func validate(settings *Settings) error {
	if settings.APIKey == "" {
		return errors.New("deepl: APIKey is required")
	}
	if settings.BaseURL == "" {
		settings.BaseURL = DefaultBaseURL(settings.APIKey)
	}
	if !absoluteHTTPURL(settings.BaseURL) {
		return errors.New("deepl: BaseURL must be an absolute http(s) URL")
	}
	if !slices.Contains([]string{"EN-US", "EN-GB"}, settings.EnglishVariant) {
		return fmt.Errorf("deepl: EnglishVariant must be EN-US or EN-GB, got %q", settings.EnglishVariant)
	}
	if !slices.Contains([]string{"", "prefer_more", "prefer_less"}, settings.Formality) {
		return fmt.Errorf("deepl: Formality must be empty, prefer_more or prefer_less, got %q", settings.Formality)
	}
	if settings.Timeout < 0 {
		return errors.New("deepl: Timeout must not be negative")
	}
	if settings.Timeout == 0 {
		settings.Timeout = DefaultTimeout
	}
	return nil
}

func (client *Client) instrument() error {
	meter := otel.Meter(instrumentationName)
	var errs [4]error
	client.characters, errs[0] = meter.Int64Counter("frappe.deepl.characters.sent",
		metric.WithDescription("Source characters sent to DeepL for translation."), metric.WithUnit("{character}"))
	client.billed, errs[1] = meter.Int64Counter("frappe.deepl.characters.billed",
		metric.WithDescription("Characters DeepL reports as billed."), metric.WithUnit("{character}"))
	client.requests, errs[2] = meter.Int64Counter("frappe.deepl.requests",
		metric.WithDescription("DeepL translate requests by outcome."))
	client.duration, errs[3] = meter.Float64Histogram("frappe.deepl.request.duration",
		metric.WithDescription("DeepL translate request latency."), metric.WithUnit("s"))
	if err := errors.Join(errs[:]...); err != nil {
		return fmt.Errorf("deepl: create metrics: %w", err)
	}
	return nil
}

type translateBody struct {
	SourceLanguage       string   `json:"source_lang,omitempty"`
	TargetLanguage       string   `json:"target_lang"`
	Context              string   `json:"context,omitempty"`
	Formality            string   `json:"formality,omitempty"`
	Text                 []string `json:"text"`
	ShowBilledCharacters bool     `json:"show_billed_characters"`
}

type translateResponse struct {
	Translations []struct {
		Text             string `json:"text"`
		BilledCharacters int64  `json:"billed_characters"`
	} `json:"translations"`
}

// Translate implements machinetranslation.Translator.
func (client *Client) Translate(ctx context.Context, request machinetranslation.Request) ([]string, error) {
	target := client.targetCode(request.Target)
	attributes := metric.WithAttributes(attribute.String("target", target))
	started := time.Now()
	translations, outcome, err := client.send(ctx, request, target)
	outcomeAttributes := metric.WithAttributes(attribute.String("target", target), attribute.String("outcome", outcome))
	client.requests.Add(ctx, 1, outcomeAttributes)
	characters := 0
	for _, text := range request.Texts {
		characters += utf8.RuneCountInString(text)
	}
	// Tagged with the outcome: only successful characters are billed.
	client.characters.Add(ctx, int64(characters), outcomeAttributes)
	client.duration.Record(ctx, time.Since(started).Seconds(), attributes)
	return translations, err
}

func (client *Client) send(ctx context.Context, request machinetranslation.Request, target string) ([]string, string, error) {
	payload, err := json.Marshal(translateBody{
		SourceLanguage: sourceCode(request.Source), TargetLanguage: target, Context: request.Context,
		Formality: client.formality, Text: request.Texts, ShowBilledCharacters: true,
	})
	if err != nil {
		return nil, "error", fmt.Errorf("deepl: encode request: %w", err)
	}
	httpRequest, err := http.NewRequestWithContext(ctx, http.MethodPost, client.endpoint, bytes.NewReader(payload))
	if err != nil {
		return nil, "error", fmt.Errorf("deepl: build request: %w", err)
	}
	httpRequest.Header.Set("Authorization", client.authorization)
	httpRequest.Header.Set("Content-Type", "application/json")
	response, err := client.http.Do(httpRequest)
	if err != nil {
		// url.Error carries the URL only; the key travels in a header.
		return nil, "transport_error", fmt.Errorf("deepl: send request: %w", err)
	}
	defer func() { _ = response.Body.Close() }()
	if response.StatusCode != http.StatusOK {
		return nil, outcomeFor(response.StatusCode), classify(response)
	}
	var decoded translateResponse
	if err := json.NewDecoder(io.LimitReader(response.Body, maximumResponseBody)).Decode(&decoded); err != nil {
		return nil, "invalid_response", fmt.Errorf("deepl: decode response: %w", err)
	}
	if len(decoded.Translations) != len(request.Texts) {
		return nil, "invalid_response", fmt.Errorf("deepl: got %d translations for %d texts", len(decoded.Translations), len(request.Texts))
	}
	translations := make([]string, len(decoded.Translations))
	var billed int64
	for index, translation := range decoded.Translations {
		translations[index] = translation.Text
		billed += translation.BilledCharacters
	}
	client.billed.Add(ctx, billed, metric.WithAttributes(attribute.String("target", target)))
	return translations, "success", nil
}

func outcomeFor(status int) string {
	switch {
	case status == http.StatusTooManyRequests || status == statusOverloaded:
		return "rate_limited"
	case status == statusQuotaExceeded:
		return "quota_exceeded"
	case status == http.StatusRequestEntityTooLarge:
		return "payload_too_large"
	case status == http.StatusUnauthorized || status == http.StatusForbidden:
		return "unauthorized"
	case status >= http.StatusInternalServerError:
		return "server_error"
	default:
		return "rejected"
	}
}

// classify turns a non-200 response into the machinetranslation error
// kinds. DeepL's JSON message is included, truncated; the request (and
// its key) never is.
func classify(response *http.Response) error {
	body, _ := io.ReadAll(io.LimitReader(response.Body, maximumErrorBody))
	message := strings.TrimSpace(string(body))
	var decoded struct {
		Message string `json:"message"`
	}
	if json.Unmarshal(body, &decoded) == nil && decoded.Message != "" {
		message = decoded.Message
	}
	cause := fmt.Errorf("deepl: status %d: %s", response.StatusCode, message)
	switch outcomeFor(response.StatusCode) {
	case "rate_limited":
		return machinetranslation.RateLimitedError{RetryAfter: retryAfter(response.Header.Get("Retry-After"))}
	case "quota_exceeded":
		return fmt.Errorf("%w: %w", machinetranslation.ErrQuotaExceeded, cause)
	case "unauthorized":
		return fmt.Errorf("%w: %w", machinetranslation.ErrUnauthorized, cause)
	case "payload_too_large":
		return fmt.Errorf("%w: %w", machinetranslation.ErrPayloadTooLarge, cause)
	case "server_error":
		return cause
	default:
		return machinetranslation.PermanentError{Cause: cause}
	}
}

// retryAfter parses a Retry-After header, in seconds or as an HTTP date.
func retryAfter(header string) time.Duration {
	if seconds, err := strconv.Atoi(strings.TrimSpace(header)); err == nil && seconds > 0 {
		return time.Duration(seconds) * time.Second
	}
	if at, err := http.ParseTime(header); err == nil {
		if wait := time.Until(at); wait > 0 {
			return wait
		}
	}
	return DefaultRetryAfter
}

// targetCode maps locale to a DeepL target code from the allowlist of
// variants DeepL supports; anything else is the bare language, so an
// unsupported variant (fr-CA, de-AT, es-MX) never reaches DeepL.
func (client *Client) targetCode(locale i18n.Locale) string {
	tag := locale.Tag()
	base, _ := tag.Base()
	region := ""
	if found, confidence := tag.Region(); confidence == language.Exact {
		region = found.String()
	}
	switch base.String() {
	case "en":
		return client.englishCode(region)
	case "pt":
		return variant(region == "BR", "PT-BR", "PT-PT")
	case "es":
		return variant(region == "419", "ES-419", "ES")
	case "zh":
		// The script is inferred when absent: zh-TW is Hant, zh is Hans.
		script, _ := tag.Script()
		return variant(script.String() == "Hant", "ZH-HANT", "ZH-HANS")
	default:
		return strings.ToUpper(base.String())
	}
}

// englishCode honors an explicit en-GB or en-US, else the configured
// variant.
func (client *Client) englishCode(region string) string {
	if region == "GB" || region == "US" {
		return "EN-" + region
	}
	return client.englishVariant
}

func variant(condition bool, when, otherwise string) string {
	if condition {
		return when
	}
	return otherwise
}

func sourceCode(locale i18n.Locale) string {
	if locale.IsZero() {
		return ""
	}
	base, _ := locale.Tag().Base()
	return strings.ToUpper(base.String())
}
