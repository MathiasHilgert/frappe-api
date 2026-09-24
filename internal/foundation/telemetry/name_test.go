package telemetry

import "testing"

func TestValidateNameAcceptsLowercaseDotSeparatedSegments(t *testing.T) {
	names := []string{
		"orders",
		"orders.created",
		"orders.created_total",
		"a",
		"a.b.c",
	}

	for _, name := range names {
		if err := validateName(name); err != nil {
			t.Errorf("validateName(%q) = %v, want nil", name, err)
		}
	}
}

func TestValidateNameRejectsInvalidNames(t *testing.T) {
	names := []string{
		"",
		"Orders",
		"orders.Created",
		"1orders",
		"orders..created",
		"orders.",
		".orders",
		"orders-created",
		"orders created",
	}

	for _, name := range names {
		if err := validateName(name); err == nil {
			t.Errorf("validateName(%q) = nil, want error", name)
		}
	}
}
