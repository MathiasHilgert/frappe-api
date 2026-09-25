package rest_test

import (
	"errors"
	"strings"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/rest"
)

type position struct {
	Name string `json:"name"`
	ID   string `json:"id"`
}

var secret = []byte("0123456789abcdef0123456789abcdef")

func newCodec(t *testing.T) *rest.CursorCodec {
	t.Helper()
	codec, err := rest.NewCursorCodec(secret)
	if err != nil {
		t.Fatal(err)
	}
	return codec
}

func TestNewCursorCodecRejectsShortSecrets(t *testing.T) {
	if _, err := rest.NewCursorCodec([]byte("short")); err == nil {
		t.Fatal("NewCursorCodec accepted a secret shorter than 32 bytes")
	}
}

func TestCursorRoundTrip(t *testing.T) {
	codec := newCodec(t)
	token, err := codec.Encode("geo.cities", position{Name: "Cordoba", ID: "3860259"})
	if err != nil {
		t.Fatal(err)
	}
	if strings.ContainsAny(token, "+/=") {
		t.Fatalf("token %q is not unpadded base64url", token)
	}
	var decoded position
	if err := codec.Decode(token, "geo.cities", &decoded); err != nil {
		t.Fatalf("Decode error = %v", err)
	}
	if decoded != (position{Name: "Cordoba", ID: "3860259"}) {
		t.Fatalf("decoded = %+v", decoded)
	}
}

func TestCursorDecodeRejectsTamperingAndMisuse(t *testing.T) {
	codec := newCodec(t)
	token, _ := codec.Encode("geo.cities", position{ID: "1"})
	other, _ := rest.NewCursorCodec([]byte("ffffffffffffffffffffffffffffffff"))
	foreign, _ := other.Encode("geo.cities", position{ID: "1"})
	payload, signature, _ := strings.Cut(token, ".")
	flipped := []byte(payload)
	flipped[2] ^= 1

	cases := map[string]struct {
		token string
		scope string
	}{
		"empty":             {"", "geo.cities"},
		"garbage":           {"not-a-cursor", "geo.cities"},
		"tampered payload":  {string(flipped) + "." + signature, "geo.cities"},
		"truncated":         {token[:len(token)-2], "geo.cities"},
		"other secret":      {foreign, "geo.cities"},
		"other scope":       {token, "geo.countries"},
		"too long":          {strings.Repeat("a", 2000), "geo.cities"},
		"invalid base64url": {"***." + signature, "geo.cities"},
	}
	for name, testCase := range cases {
		t.Run(name, func(t *testing.T) {
			var decoded position
			if err := codec.Decode(testCase.token, testCase.scope, &decoded); !errors.Is(err, rest.ErrInvalidCursor) {
				t.Fatalf("Decode error = %v, want ErrInvalidCursor", err)
			}
		})
	}
}
