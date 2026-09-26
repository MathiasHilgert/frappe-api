//go:build integration

package migrations_test

import (
	"context"
	"errors"
	"testing"

	"github.com/jackc/pgx/v5/pgconn"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/database/databasetest"
)

func TestIntegrationGeoDataRevisionIsReadOnlyForTheApplication(t *testing.T) {
	t.Parallel()
	pool := databasetest.New(t)
	ctx := context.Background()

	var revision string
	if err := pool.QueryRow(ctx, "SELECT revision FROM geo_data_versions").Scan(&revision); err != nil {
		t.Fatalf("read revision: %v", err)
	}
	if revision != "geo-seed-3" {
		t.Errorf("revision = %q, want geo-seed-3", revision)
	}
	_, err := pool.Exec(ctx, "UPDATE geo_data_versions SET revision = 'forged'")
	var postgresError *pgconn.PgError
	if !errors.As(err, &postgresError) || postgresError.Code != insufficientPrivilege {
		t.Errorf("application UPDATE error = %v, want insufficient privilege", err)
	}
}
