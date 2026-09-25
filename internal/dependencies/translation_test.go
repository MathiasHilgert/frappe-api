package dependencies

import (
	"bytes"
	"context"
	"log/slog"
	"strings"
	"testing"
	"time"

	"github.com/jackc/pgx/v5/pgxpool"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/jobs"
)

func TestTranslatorFromIsDisabledWithoutAKeyAndSaysSo(t *testing.T) {
	var output bytes.Buffer
	translator, err := translatorFrom(configuration.DeepL{}, slog.New(slog.NewTextHandler(&output, nil)))
	if err != nil || translator != nil {
		t.Fatalf("translatorFrom without a key = %v, %v; want nil, nil", translator, err)
	}
	if !strings.Contains(output.String(), "DEEPL_API_KEY") {
		t.Fatalf("log = %q, want a line naming DEEPL_API_KEY", output.String())
	}
}

func TestTranslatorFromBuildsDeepLWithAKey(t *testing.T) {
	translator, err := translatorFrom(configuration.DeepL{APIKey: "key:fx", EnglishVariant: "EN-GB", Timeout: time.Second, BatchSize: 10}, slog.Default())
	if err != nil || translator == nil {
		t.Fatalf("translatorFrom = %v, %v; want a translator", translator, err)
	}
}

func TestProvideInternationalizationWiresMachineTranslationJobs(t *testing.T) {
	loaded := configuration.Configuration{
		Internationalization: configuration.Internationalization{SourceLocale: "es-419", SupportedLocales: []string{"es-419", "en"}},
		DeepL:                configuration.DeepL{APIKey: "key:fx", EnglishVariant: "EN-US", Timeout: time.Second, BatchSize: 10},
	}
	catalog := jobs.NewCatalog()
	_, service, err := provideInternationalization(loaded, catalog.Module(localizedTextsJobsModule), &application.Handle[*pgxpool.Pool]{})
	if err != nil || service == nil {
		t.Fatalf("provideInternationalization = %v, %v", service, err)
	}
	if err := catalog.Validate(); err != nil {
		t.Fatalf("catalog.Validate() = %v; every job must be handled", err)
	}
	if len(catalog.Schedules()) != 2 {
		t.Fatalf("schedules = %d, want the expired and orphan sweeps", len(catalog.Schedules()))
	}
}

func TestTenantTransactorFailsBeforeTheDatabaseIsReady(t *testing.T) {
	transactor := tenantTransactor(&application.Handle[*pgxpool.Pool]{})
	if err := transactor(context.Background(), "acme", func(context.Context) error { return nil }); err == nil {
		t.Fatal("transactor without a pool = nil error")
	}
}

func TestTransactionTenantIsUnknownOutsideATransaction(t *testing.T) {
	if tenant, found := transactionTenant(context.Background()); found || tenant != "" {
		t.Fatalf("transactionTenant = %q, %v; want none", tenant, found)
	}
}
