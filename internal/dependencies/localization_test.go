package dependencies

import (
	"context"
	"errors"
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/jackc/pgx/v5/pgxpool"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
)

func TestProvideLocalizationBuildsTheEmbeddedCatalog(t *testing.T) {
	catalog, err := provideLocalization(configuration.Internationalization{
		SourceLocale:     "es-419",
		SupportedLocales: []string{"es-419", "en", "pt-BR", "fr", "it", "de", "ru", "zh-Hans", "ko", "ja"},
	})
	if err != nil {
		t.Fatalf("provideLocalization returned unexpected error: %v", err)
	}
	recorder := httptest.NewRecorder()
	request := httptest.NewRequest(http.MethodGet, "/v1/x", nil)
	request.Header.Set("Accept-Language", "ru-RU")
	catalog.Middleware(http.NotFoundHandler()).ServeHTTP(recorder, request)
	if got := recorder.Header().Get("Content-Language"); got != "ru" {
		t.Fatalf("Content-Language = %q, want ru", got)
	}
}

func TestProvideLocalizedTextsBuildsTheServiceOnTheCatalog(t *testing.T) {
	catalog, err := provideLocalization(configuration.Internationalization{
		SourceLocale: "es-419", SupportedLocales: []string{"es-419", "en"},
	})
	if err != nil {
		t.Fatalf("provideLocalization: %v", err)
	}
	service, err := provideLocalizedTexts(catalog, nil)
	if err != nil || service == nil {
		t.Fatalf("provideLocalizedTexts = %v, %v; want a service", service, err)
	}
}

func TestLocaleCheckFailsWhileTheDatabaseIsNotReady(t *testing.T) {
	catalog, err := provideLocalization(configuration.Internationalization{
		SourceLocale: "es-419", SupportedLocales: []string{"es-419", "en"},
	})
	if err != nil {
		t.Fatalf("provideLocalization: %v", err)
	}
	if _, err := checkLocales(&application.Handle[*pgxpool.Pool]{}, catalog)(context.Background()); !errors.Is(err, errDatabaseNotReady) {
		t.Fatalf("checkLocales before the pool is up error = %v, want errDatabaseNotReady", err)
	}
}

func TestProvideLocalizationRejectsALocaleWithoutCatalog(t *testing.T) {
	if _, err := provideLocalization(configuration.Internationalization{
		SourceLocale: "es-419", SupportedLocales: []string{"es-419", "he"},
	}); err == nil {
		t.Fatal("provideLocalization with a locale lacking a catalog returned nil error")
	}
}
