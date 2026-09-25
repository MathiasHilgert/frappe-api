package dependencies

import (
	"errors"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/rest"
)

func TestProvideCursorCodecSharesAConfiguredSecretAcrossReplicas(t *testing.T) {
	settings := configuration.HTTP{CursorSecret: "0123456789abcdef0123456789abcdef"}
	first, err := provideCursorCodec(settings)
	if err != nil {
		t.Fatal(err)
	}
	second, err := provideCursorCodec(settings)
	if err != nil {
		t.Fatal(err)
	}
	token, _ := first.Encode("scope", 1)
	var position int
	if err := second.Decode(token, "scope", &position); err != nil {
		t.Fatalf("a replica with the same secret rejected the cursor: %v", err)
	}
}

func TestProvideCursorCodecUsesARandomSecretWhenNoneIsConfigured(t *testing.T) {
	first, err := provideCursorCodec(configuration.HTTP{})
	if err != nil {
		t.Fatal(err)
	}
	second, _ := provideCursorCodec(configuration.HTTP{})
	token, _ := first.Encode("scope", 1)
	var position int
	if err := first.Decode(token, "scope", &position); err != nil {
		t.Fatalf("the issuing codec rejected its own cursor: %v", err)
	}
	if err := second.Decode(token, "scope", &position); !errors.Is(err, rest.ErrInvalidCursor) {
		t.Fatalf("two random secrets were equal: Decode error = %v", err)
	}
}
