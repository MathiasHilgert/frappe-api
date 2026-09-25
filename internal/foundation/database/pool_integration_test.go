//go:build integration

package database_test

import (
	"context"
	"testing"
	"time"

	"github.com/testcontainers/testcontainers-go/modules/postgres"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/database"
)

// startPostgresContainer starts a disposable Postgres container for one
// test and returns a connection string for the given role's credentials.
// It is not executed by "go vet -tags=integration ./..." or a plain
// "go build" (Docker is not available in every environment that builds
// this module); it only runs under "task test:integration" / CI, which do
// have Docker.
func startPostgresContainer(t *testing.T, username, password string) string {
	t.Helper()

	ctx := context.Background()
	container, err := postgres.Run(ctx, "postgres:18.1",
		postgres.WithDatabase("frappe"),
		postgres.WithUsername(username),
		postgres.WithPassword(password),
		postgres.BasicWaitStrategies(),
	)
	if err != nil {
		t.Fatalf("start postgres container: %v", err)
	}
	t.Cleanup(func() {
		if terminateErr := container.Terminate(context.Background()); terminateErr != nil {
			t.Logf("terminate postgres container: %v", terminateErr)
		}
	})

	connectionString, err := container.ConnectionString(ctx, "sslmode=disable")
	if err != nil {
		t.Fatalf("get postgres connection string: %v", err)
	}
	return connectionString
}

func TestIntegrationUpChecksAndDownAPool(t *testing.T) {
	connectionString := startPostgresContainer(t, "frappe_application", "frappe_application")

	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()

	pool, err := database.Up(ctx, database.Settings{URL: connectionString})
	if err != nil {
		t.Fatalf("Up returned unexpected error: %v", err)
	}

	if err := database.Check(ctx, pool); err != nil {
		t.Fatalf("Check returned unexpected error: %v", err)
	}

	if err := database.Down(ctx, pool); err != nil {
		t.Fatalf("Down returned unexpected error: %v", err)
	}
}

func TestIntegrationUpFailsFastOnAnUnreachableDatabase(t *testing.T) {
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()

	_, err := database.Up(ctx, database.Settings{
		URL:            "postgres://user:password@127.0.0.1:1/frappe?sslmode=disable",
		ConnectTimeout: 500 * time.Millisecond,
	})
	if err == nil {
		t.Fatal("Up returned nil error against an unroutable address")
	}
}
