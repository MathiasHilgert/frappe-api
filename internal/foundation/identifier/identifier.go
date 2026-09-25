// Package identifier creates and validates the prefixed, opaque public
// identifiers of business entities, such as "dish_06f3vdz0q9x7k2m4n8p5r1s3tw".
//
// # Format
//
// An identifier is "<prefix>_<suffix>":
//
//   - prefix names the entity type (Stripe style: "cus_", "pi_"): 2 to 16
//     lowercase ASCII letters or digits, starting with a letter. It makes
//     identifiers self-describing in logs and support tickets, and lets
//     Parse reject an identifier of the wrong type ("rest_..." where a
//     "dish_..." is expected) before any database lookup.
//   - suffix is a UUIDv7 (RFC 9562) encoded as 26 lowercase Crockford
//     base32 characters, without padding.
//
// # Why UUIDv7 in Crockford base32
//
// UUIDv7 is time ordered, so new rows append to the end of B-tree indexes
// instead of scattering like UUIDv4, and it is stored natively in a
// Postgres uuid column (16 bytes): the prefix is presentation only and is
// never stored. It needs no new dependency (github.com/google/uuid) and
// no coordination between replicas. Crockford base32 is shorter than the
// 36 character hex form, URL safe, case free once canonicalized to
// lowercase, avoids the ambiguous letters i, l, o and u, and, because its
// alphabet is in ASCII order and the encoding is most significant bit
// first, keeps the time ordering of the UUID in the string. The
// identifier carries its creation time with millisecond precision; that
// is acceptable for business entities (they expose created_at anyway),
// and random bits (74) keep it unguessable.
//
// Reference data keyed by a natural, public standard (ISO 3166 codes,
// GeoNames ids, IANA time zones) does not use this package; see
// docs/api-conventions.md.
package identifier

import (
	"encoding/base32"
	"errors"
	"fmt"
	"regexp"
	"strings"

	"github.com/google/uuid"
)

// ErrInvalid reports an identifier that is malformed or has the wrong
// prefix. Adapters translate it into a 404 problem when it is a path
// parameter (no such resource can exist), and into a 422 problem with an
// errors[] entry when it is a body or query value
// (docs/api-conventions.md, "Errors").
var ErrInvalid = errors.New("invalid identifier")

// suffixLength is the length of a 16 byte UUID in unpadded base32.
const suffixLength = 26

// version7 is the only UUID version New creates and Parse accepts.
const version7 = 7

// alphabet is Crockford's base32 alphabet in lowercase.
const alphabet = "0123456789abcdefghjkmnpqrstvwxyz"

// encoding is unpadded lowercase Crockford base32. Parse re-encodes what
// it decoded, so a suffix whose unused trailing bits are not zero is
// rejected: every UUID has exactly one accepted string form.
var encoding = base32.NewEncoding(alphabet).WithPadding(base32.NoPadding)

var prefixPattern = regexp.MustCompile(`^[a-z][a-z0-9]{1,15}$`)

// New returns a new identifier with prefix. It panics if prefix is not 2
// to 16 lowercase letters or digits starting with a letter, because a
// prefix is a compile time constant of the calling module, never input.
func New(prefix string) string {
	mustBeValidPrefix(prefix)
	value, err := uuid.NewV7()
	if err != nil {
		// uuid.NewV7 only fails when the system random source fails,
		// which leaves the process unable to create any identifier.
		panic(fmt.Sprintf("identifier: create UUIDv7: %v", err))
	}
	return Format(prefix, value)
}

// Format returns the identifier for value with prefix. It is used to
// render identifiers read from storage, which keeps the raw UUID.
func Format(prefix string, value uuid.UUID) string {
	mustBeValidPrefix(prefix)
	return prefix + "_" + encoding.EncodeToString(value[:])
}

// Parse returns the UUID of an identifier that carries prefix. It returns
// an error wrapping ErrInvalid when value has another prefix, is not in
// canonical form, or does not hold a UUIDv7.
func Parse(prefix, value string) (uuid.UUID, error) {
	mustBeValidPrefix(prefix)
	suffix, found := strings.CutPrefix(value, prefix+"_")
	if !found || len(suffix) != suffixLength {
		return uuid.UUID{}, fmt.Errorf("%w: want a %q identifier", ErrInvalid, prefix)
	}
	decoded, err := encoding.DecodeString(suffix)
	if err != nil {
		return uuid.UUID{}, fmt.Errorf("%w: %w", ErrInvalid, err)
	}
	if encoding.EncodeToString(decoded) != suffix {
		return uuid.UUID{}, fmt.Errorf("%w: not in canonical form", ErrInvalid)
	}
	parsed, err := uuid.FromBytes(decoded)
	if err != nil || parsed.Version() != version7 || parsed.Variant() != uuid.RFC4122 {
		return uuid.UUID{}, fmt.Errorf("%w: not a version 7 UUID", ErrInvalid)
	}
	return parsed, nil
}

// Valid reports whether value is a well-formed identifier with prefix.
func Valid(prefix, value string) bool {
	_, err := Parse(prefix, value)
	return err == nil
}

func mustBeValidPrefix(prefix string) {
	if !prefixPattern.MatchString(prefix) {
		panic(fmt.Sprintf("identifier: invalid prefix %q: want 2 to 16 lowercase letters or digits starting with a letter", prefix))
	}
}
