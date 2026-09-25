package deepl_test

import (
	"context"
	"encoding/json"
	"errors"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/machinetranslation"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/machinetranslation/deepl"
)

const testKey = "secret-key:fx"

type captured struct {
	body          map[string]any
	authorization string
	path          string
	contentType   string
}

func newServer(t *testing.T, status int, headers map[string]string, response string) (*httptest.Server, *captured) {
	t.Helper()
	seen := &captured{}
	server := httptest.NewServer(http.HandlerFunc(func(writer http.ResponseWriter, request *http.Request) {
		seen.authorization = request.Header.Get("Authorization")
		seen.path = request.URL.Path
		seen.contentType = request.Header.Get("Content-Type")
		if err := json.NewDecoder(request.Body).Decode(&seen.body); err != nil {
			t.Errorf("decode body: %v", err)
		}
		for name, value := range headers {
			writer.Header().Set(name, value)
		}
		writer.WriteHeader(status)
		_, _ = writer.Write([]byte(response))
	}))
	t.Cleanup(server.Close)
	return server, seen
}

func newClient(t *testing.T, baseURL string, mutate ...func(*deepl.Settings)) *deepl.Client {
	t.Helper()
	settings := deepl.Settings{APIKey: testKey, BaseURL: baseURL, EnglishVariant: "EN-US", Timeout: 5 * time.Second}
	for _, change := range mutate {
		change(&settings)
	}
	client, err := deepl.New(settings)
	if err != nil {
		t.Fatalf("New: %v", err)
	}
	return client
}

func request(source, target string, texts ...string) machinetranslation.Request {
	return machinetranslation.Request{
		Source: i18n.MustParseLocale(source), Target: i18n.MustParseLocale(target),
		Context: "Description of a dish on a restaurant menu", Texts: texts,
	}
}

func TestTranslateSendsABatchWithContextAndReturnsTranslationsInOrder(t *testing.T) {
	server, seen := newServer(t, http.StatusOK, nil,
		`{"translations":[{"text":"Hello","billed_characters":4},{"text":"World","billed_characters":5}]}`)
	client := newClient(t, server.URL, func(settings *deepl.Settings) { settings.Formality = "prefer_less" })

	translated, err := client.Translate(context.Background(), request("es-419", "fr", "Hola", "Mundo"))
	if err != nil {
		t.Fatalf("Translate: %v", err)
	}
	if strings.Join(translated, "|") != "Hello|World" {
		t.Fatalf("translations = %v", translated)
	}
	if seen.path != "/v2/translate" {
		t.Errorf("path = %q, want /v2/translate", seen.path)
	}
	if seen.authorization != "DeepL-Auth-Key "+testKey {
		t.Errorf("authorization = %q", seen.authorization)
	}
	if seen.contentType != "application/json" {
		t.Errorf("content type = %q", seen.contentType)
	}
	want := map[string]any{
		"source_lang": "ES", "target_lang": "FR", "context": "Description of a dish on a restaurant menu",
		"formality": "prefer_less", "show_billed_characters": true,
	}
	for key, value := range want {
		if seen.body[key] != value {
			t.Errorf("body[%s] = %v, want %v", key, seen.body[key], value)
		}
	}
	texts, _ := seen.body["text"].([]any)
	if len(texts) != 2 || texts[0] != "Hola" || texts[1] != "Mundo" {
		t.Errorf("body text = %v", seen.body["text"])
	}
}

func TestTranslateMapsTargetLocalesToDeepLCodes(t *testing.T) {
	cases := map[string]string{
		"en": "EN-GB", "es-419": "ES-419", "pt-BR": "PT-BR", "zh-Hans": "ZH-HANS",
		"de": "DE", "ja": "JA", "ko": "KO", "ru": "RU", "it": "IT",
	}
	for locale, want := range cases {
		t.Run(locale, func(t *testing.T) {
			server, seen := newServer(t, http.StatusOK, nil, `{"translations":[{"text":"x"}]}`)
			client := newClient(t, server.URL, func(settings *deepl.Settings) { settings.EnglishVariant = "EN-GB" })
			if _, err := client.Translate(context.Background(), request("fr", locale, "bonjour")); err != nil {
				t.Fatalf("Translate: %v", err)
			}
			if seen.body["target_lang"] != want {
				t.Fatalf("target_lang = %v, want %s", seen.body["target_lang"], want)
			}
		})
	}
}

func TestTranslateOmitsEmptyContextAndFormality(t *testing.T) {
	server, seen := newServer(t, http.StatusOK, nil, `{"translations":[{"text":"x"}]}`)
	client := newClient(t, server.URL)
	translation := request("es-419", "en", "hola")
	translation.Context = ""
	if _, err := client.Translate(context.Background(), translation); err != nil {
		t.Fatalf("Translate: %v", err)
	}
	for _, key := range []string{"context", "formality"} {
		if _, present := seen.body[key]; present {
			t.Errorf("body has %s, want it omitted", key)
		}
	}
}

func TestTranslateClassifiesFailures(t *testing.T) {
	t.Run("429 with Retry-After is rate limited", func(t *testing.T) {
		server, _ := newServer(t, http.StatusTooManyRequests, map[string]string{"Retry-After": "7"}, `{"message":"Too many requests"}`)
		_, err := newClient(t, server.URL).Translate(context.Background(), request("es-419", "en", "hola"))
		var limited machinetranslation.RateLimitedError
		if !errors.As(err, &limited) || limited.RetryAfter != 7*time.Second {
			t.Fatalf("err = %v, want RateLimitedError after 7s", err)
		}
	})
	t.Run("429 without Retry-After uses the default", func(t *testing.T) {
		server, _ := newServer(t, http.StatusTooManyRequests, nil, ``)
		_, err := newClient(t, server.URL).Translate(context.Background(), request("es-419", "en", "hola"))
		var limited machinetranslation.RateLimitedError
		if !errors.As(err, &limited) || limited.RetryAfter != deepl.DefaultRetryAfter {
			t.Fatalf("err = %v, want RateLimitedError after the default", err)
		}
	})
	t.Run("456 is quota exceeded", func(t *testing.T) {
		server, _ := newServer(t, 456, nil, `{"message":"Quota exceeded"}`)
		_, err := newClient(t, server.URL).Translate(context.Background(), request("es-419", "en", "hola"))
		if !errors.Is(err, machinetranslation.ErrQuotaExceeded) {
			t.Fatalf("err = %v, want ErrQuotaExceeded", err)
		}
	})
	for _, status := range []int{http.StatusBadRequest, http.StatusForbidden, http.StatusRequestEntityTooLarge} {
		t.Run(http.StatusText(status)+" is permanent", func(t *testing.T) {
			server, _ := newServer(t, status, nil, `{"message":"nope"}`)
			_, err := newClient(t, server.URL).Translate(context.Background(), request("es-419", "en", "hola"))
			var permanent machinetranslation.PermanentError
			if !errors.As(err, &permanent) {
				t.Fatalf("err = %v, want PermanentError", err)
			}
			if strings.Contains(err.Error(), testKey) {
				t.Fatalf("error leaks the API key: %v", err)
			}
		})
	}
	t.Run("503 is transient", func(t *testing.T) {
		server, _ := newServer(t, http.StatusServiceUnavailable, nil, ``)
		_, err := newClient(t, server.URL).Translate(context.Background(), request("es-419", "en", "hola"))
		var permanent machinetranslation.PermanentError
		var limited machinetranslation.RateLimitedError
		if err == nil || errors.As(err, &permanent) || errors.As(err, &limited) || errors.Is(err, machinetranslation.ErrQuotaExceeded) {
			t.Fatalf("err = %v, want a plain transient error", err)
		}
	})
	t.Run("a count mismatch is transient", func(t *testing.T) {
		server, _ := newServer(t, http.StatusOK, nil, `{"translations":[]}`)
		if _, err := newClient(t, server.URL).Translate(context.Background(), request("es-419", "en", "hola")); err == nil {
			t.Fatal("err = nil, want a mismatch error")
		}
	})
}

func TestNewValidatesSettings(t *testing.T) {
	cases := map[string]deepl.Settings{
		"missing key":      {BaseURL: "https://api-free.deepl.com", EnglishVariant: "EN-US"},
		"bad url":          {APIKey: testKey, BaseURL: "::", EnglishVariant: "EN-US"},
		"bad variant":      {APIKey: testKey, BaseURL: "https://api-free.deepl.com", EnglishVariant: "EN"},
		"bad formality":    {APIKey: testKey, BaseURL: "https://api-free.deepl.com", EnglishVariant: "EN-US", Formality: "more"},
		"negative timeout": {APIKey: testKey, BaseURL: "https://api-free.deepl.com", EnglishVariant: "EN-US", Timeout: -1},
	}
	for name, settings := range cases {
		t.Run(name, func(t *testing.T) {
			if _, err := deepl.New(settings); err == nil {
				t.Fatal("New() error = nil")
			} else if strings.Contains(err.Error(), testKey) {
				t.Fatalf("error leaks the API key: %v", err)
			}
		})
	}
}

func TestDefaultBaseURLFollowsTheKeyKind(t *testing.T) {
	if got := deepl.DefaultBaseURL("abc:fx"); got != "https://api-free.deepl.com" {
		t.Errorf("free key base URL = %q", got)
	}
	if got := deepl.DefaultBaseURL("abc"); got != "https://api.deepl.com" {
		t.Errorf("pro key base URL = %q", got)
	}
}
