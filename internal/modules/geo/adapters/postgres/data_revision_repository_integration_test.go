//go:build integration

package postgres_test

import (
	"context"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/adapters/postgres"
)

func TestIntegrationDataRevisionRepositoryReadsTheSnapshotRevision(t *testing.T) {
	t.Parallel()
	revision, err := postgres.NewDataRevisionRepository(newReadyPool(t)).DataRevision(context.Background())
	if err != nil || revision != "geo-seed-3" {
		t.Fatalf("DataRevision = %q, %v; want geo-seed-3", revision, err)
	}
}
