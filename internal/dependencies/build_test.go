package dependencies_test

import (
	"context"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/dependencies"
)

// TestNewApplicationBuildsAnApplicationThatStartsAndStops verifies that the
// composition root produces an Application that can be started and stopped
// without error, even before any real module exists.
func TestNewApplicationBuildsAnApplicationThatStartsAndStops(t *testing.T) {
	application := dependencies.NewApplication()
	if application == nil {
		t.Fatal("NewApplication returned nil")
	}

	if err := application.Up(context.Background()); err != nil {
		t.Fatalf("Up returned unexpected error: %v", err)
	}
	if err := application.Down(context.Background()); err != nil {
		t.Fatalf("Down returned unexpected error: %v", err)
	}
}
