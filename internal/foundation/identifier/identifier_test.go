package identifier_test

import (
	"errors"
	"regexp"
	"slices"
	"strings"
	"testing"

	"github.com/google/uuid"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/identifier"
)

var canonical = regexp.MustCompile(`^dish_[0-9a-hjkmnp-tv-z]{26}$`)

func TestNewReturnsPrefixedCanonicalIdentifier(t *testing.T) {
	value := identifier.New("dish")
	if !canonical.MatchString(value) {
		t.Fatalf("New() = %q, want dish_ followed by 26 lowercase Crockford base32 characters", value)
	}
}

func TestNewIsUniqueAndSortsByCreation(t *testing.T) {
	values := make([]string, 0, 1000)
	for range 1000 {
		values = append(values, identifier.New("dish"))
	}
	if !slices.IsSorted(values) {
		t.Fatal("identifiers created in sequence are not lexicographically sorted")
	}
	if len(slices.Compact(slices.Clone(values))) != len(values) {
		t.Fatal("New returned a duplicate identifier")
	}
}

func TestParseRoundTripsTheUnderlyingUUID(t *testing.T) {
	value := identifier.New("rest")
	parsed, err := identifier.Parse("rest", value)
	if err != nil {
		t.Fatalf("Parse(%q) error = %v", value, err)
	}
	if parsed.Version() != 7 {
		t.Fatalf("parsed version = %d, want 7", parsed.Version())
	}
	if got := identifier.Format("rest", parsed); got != value {
		t.Fatalf("Format(Parse(%q)) = %q", value, got)
	}
}

func TestParseRejectsMalformedIdentifiers(t *testing.T) {
	valid := identifier.New("dish")
	suffix := strings.TrimPrefix(valid, "dish_")
	cases := map[string]string{
		"wrong prefix":     "rest_" + suffix,
		"missing prefix":   suffix,
		"uppercase":        "dish_" + strings.ToUpper(suffix),
		"too short":        "dish_" + suffix[:25],
		"too long":         valid + "0",
		"excluded letter":  "dish_" + "u" + suffix[1:],
		"not version 7":    identifier.Format("dish", uuid.New()),
		"empty":            "",
		"overflowing bits": "dish_" + suffix[:25] + "z",
	}
	for name, value := range cases {
		t.Run(name, func(t *testing.T) {
			if _, err := identifier.Parse("dish", value); !errors.Is(err, identifier.ErrInvalid) {
				t.Fatalf("Parse(%q) error = %v, want ErrInvalid", value, err)
			}
			if identifier.Valid("dish", value) {
				t.Fatalf("Valid(%q) = true", value)
			}
		})
	}
}

func TestNewPanicsOnInvalidPrefix(t *testing.T) {
	for _, prefix := range []string{"", "d", "Dish", "dish_item", "toolongprefixvalue", "1dish"} {
		t.Run(prefix, func(t *testing.T) {
			defer func() {
				if recover() == nil {
					t.Fatalf("New(%q) did not panic", prefix)
				}
			}()
			identifier.New(prefix)
		})
	}
}
