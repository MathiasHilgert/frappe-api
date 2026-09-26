//go:build integration

package postgres_test

import (
	"context"
	"errors"
	"testing"

	"github.com/jackc/pgx/v5/pgxpool"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/database/databasetest"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/adapters/postgres"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
)

// GeoNames ids of the fixtures these tests read from the seeded snapshot.
const (
	cordobaProvince = int64(3860255)
	cordobaCity     = int64(3860259)
	buenosAires     = int64(3435910)
	saoPaulo        = int64(3448439)
)

var (
	spanish  = i18n.MustParseLocale("es-419")
	japanese = i18n.MustParseLocale("ja")
	// noLocale reads every name in the place's own name.
	noLocale i18n.Locale
)

// readyPool is a fresh seeded database, connected as the read-only
// application role.
type readyPool struct{ pool *pgxpool.Pool }

func newReadyPool(t *testing.T) readyPool {
	t.Helper()
	return readyPool{pool: databasetest.New(t)}
}

func (source readyPool) Get() (*pgxpool.Pool, bool) { return source.pool, true }

type notReadyPool struct{}

func (notReadyPool) Get() (*pgxpool.Pool, bool) { return nil, false }

func TestIntegrationRepositoriesReportAPoolThatIsNotUp(t *testing.T) {
	t.Parallel()
	repository := postgres.NewCountryRepository(notReadyPool{})

	if _, err := repository.Countries(context.Background(), spanish, application.CountryFilter{Limit: 1}); !errors.Is(err, postgres.ErrPoolUnavailable) {
		t.Fatalf("error = %v, want ErrPoolUnavailable", err)
	}
}
