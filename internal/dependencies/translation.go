package dependencies

import (
	"context"
	"fmt"
	"log/slog"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgxpool"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/database"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/machinetranslation"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/machinetranslation/deepl"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/jobs"
)

// localizedTextsJobsModule scopes the localized text jobs:
// localized_texts.machine_translate and the two periodic sweeps.
const localizedTextsJobsModule = "localized_texts"

// tenantSetting is the transaction-local Row Level Security setting
// localizedtext, and every tenant scoped table, relies on.
const tenantSetting = "application.tenant"

// jobsTenancy captures the tenant of the enqueuing transaction (its
// application.tenant setting) into the job, so a handler can open a
// transaction for that tenant again (machine translation does). There is
// no tenant model yet; the transaction setting is the only source of
// truth, and Bind is left nil: handlers read Job.Tenant.
var jobsTenancy = jobs.Tenancy{Resolve: transactionTenant}

// transactionTenant returns the application.tenant setting of the
// transaction in ctx, if any.
func transactionTenant(ctx context.Context) (string, bool) {
	transaction, ok := database.TransactionFromContext(ctx)
	if !ok {
		return "", false
	}
	var tenant string
	if err := transaction.QueryRow(ctx, "SELECT coalesce(current_setting($1, true), '')", tenantSetting).Scan(&tenant); err != nil {
		return "", false
	}
	return tenant, tenant != ""
}

// tenantTransactor opens application pool transactions scoped to a
// tenant, for the machine translation jobs.
func tenantTransactor(pool *application.Handle[*pgxpool.Pool]) machinetranslation.Transactor {
	return func(ctx context.Context, tenant string, work func(ctx context.Context) error) error {
		connected, ready := pool.Get()
		if !ready || connected == nil {
			return errDatabaseNotReady
		}
		settings := database.TransactionSettings{}
		if tenant != "" {
			settings[tenantSetting] = tenant
		}
		return database.WithinTransaction(ctx, connected, settings, func(ctx context.Context, _ pgx.Tx) error {
			return work(ctx)
		})
	}
}

// translatorFrom builds the DeepL translator, or returns nil (machine
// translation disabled) without DEEPL_API_KEY.
func translatorFrom(settings configuration.DeepL, logger *slog.Logger) (machinetranslation.Translator, error) {
	if settings.APIKey == "" {
		logger.Info("machine translation disabled: DEEPL_API_KEY is empty; localized texts fall back to their source")
		return nil, nil
	}
	client, err := deepl.New(deepl.Settings{
		APIKey:         settings.APIKey,
		BaseURL:        settings.BaseURL,
		EnglishVariant: settings.EnglishVariant,
		Formality:      settings.Formality,
		Timeout:        settings.Timeout,
	})
	if err != nil {
		return nil, fmt.Errorf("deepl: %w", err)
	}
	return client, nil
}

// provideMachineTranslation defines the localized text jobs on module;
// the caller registers their handlers once the Service exists.
func provideMachineTranslation(loaded configuration.Configuration, module *jobs.Module, pool *application.Handle[*pgxpool.Pool]) (*machinetranslation.MachineTranslation, error) {
	translator, err := translatorFrom(loaded.DeepL, slog.Default())
	if err != nil {
		return nil, err
	}
	sweeps := loaded.LocalizedTexts
	return machinetranslation.New(module, machinetranslation.Settings{
		Translator:           translator,
		Transactor:           tenantTransactor(pool),
		BatchSize:            loaded.DeepL.BatchSize,
		ExpiredSweepInterval: sweeps.ExpiredSweepInterval,
		OrphanSweepInterval:  sweeps.OrphanSweepInterval,
		OrphanMinimumAge:     sweeps.OrphanMinimumAge,
		SweepLimit:           sweeps.SweepLimit,
		QuotaPause:           loaded.DeepL.QuotaPause,
	})
}
