//go:build integration

package database_test

import (
	"context"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/database"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/database/databasetest"
)

func TestIntegrationUpChecksAndDownAPool(t *testing.T) {
	t.Parallel()

	pool := databasetest.New(t)

	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()

	if err := database.Check(ctx, pool); err != nil {
		t.Fatalf("Check returned unexpected error: %v", err)
	}
	if err := database.Down(ctx, pool); err != nil {
		t.Fatalf("Down returned unexpected error: %v", err)
	}
}

func TestIntegrationUpFailsFastOnAnUnreachableDatabase(t *testing.T) {
	t.Parallel()

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
