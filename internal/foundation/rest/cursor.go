package rest

import (
	"crypto/hmac"
	"crypto/sha256"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"strings"
)

// ErrInvalidCursor reports a cursor that is malformed, was tampered with,
// was signed with another secret, or belongs to another collection.
var ErrInvalidCursor = errors.New("invalid cursor")

// MinimumCursorSecretBytes is the shortest secret NewCursorCodec accepts:
// the output size of HMAC-SHA256, per RFC 2104's key length guidance.
const MinimumCursorSecretBytes = 32

// MaximumCursorBytes bounds a cursor before it is decoded, so a huge
// query parameter is rejected before any allocation proportional to it.
const MaximumCursorBytes = 1024

// cursorVersion is the payload format version. A payload with another
// version is rejected, so the format can change without old cursors
// being misread: clients simply restart from the first page.
const cursorVersion = 1

// cursorPayload is what a cursor carries, before signing.
type cursorPayload struct {
	Scope    string          `json:"s"`
	Position json.RawMessage `json:"p"`
	Version  int             `json:"v"`
}

// CursorCodec encodes and decodes opaque pagination cursors.
//
// A cursor is base64url(JSON payload) "." base64url(HMAC-SHA256), both
// unpadded. The payload holds a format version, the collection scope and
// the keyset position (the sort key values of the last returned row).
//
// Why signed: a cursor is client input that ends up in a WHERE clause. An
// unsigned cursor lets clients forge arbitrary positions, probe sort keys
// of rows they cannot list, or reuse a cursor of one collection or filter
// on another. The HMAC makes it opaque in practice (clients cannot build
// one) and lets Decode reject every tampered, foreign or cross
// collection cursor with one constant time comparison, without a
// database round trip. It is not encrypted: the position is not secret
// (it only contains values the client already received), so
// confidentiality would add cost without benefit.
type CursorCodec struct {
	secret []byte
}

// NewCursorCodec returns a codec that signs with secret, which must be at
// least MinimumCursorSecretBytes long and shared by every replica.
func NewCursorCodec(secret []byte) (*CursorCodec, error) {
	if len(secret) < MinimumCursorSecretBytes {
		return nil, fmt.Errorf("cursor secret must be at least %d bytes, got %d", MinimumCursorSecretBytes, len(secret))
	}
	return &CursorCodec{secret: append([]byte(nil), secret...)}, nil
}

// Encode returns the cursor for position in scope. scope names the
// collection and, when the collection is filtered, the filter values
// (for example "geo.cities?country=AR"), so a cursor only works on the
// exact listing that produced it. position must marshal to JSON.
func (codec *CursorCodec) Encode(scope string, position any) (string, error) {
	encodedPosition, err := json.Marshal(position)
	if err != nil {
		return "", fmt.Errorf("encode cursor position: %w", err)
	}
	payload, err := json.Marshal(cursorPayload{Version: cursorVersion, Scope: scope, Position: encodedPosition})
	if err != nil {
		return "", fmt.Errorf("encode cursor: %w", err)
	}
	encodedPayload := base64.RawURLEncoding.EncodeToString(payload)
	return encodedPayload + "." + base64.RawURLEncoding.EncodeToString(codec.sign(encodedPayload)), nil
}

// Decode verifies token and unmarshals its position into target. It
// returns an error wrapping ErrInvalidCursor unless token was produced by
// Encode with the same secret, format version and scope.
func (codec *CursorCodec) Decode(token, scope string, target any) error {
	if len(token) > MaximumCursorBytes {
		return fmt.Errorf("%w: too long", ErrInvalidCursor)
	}
	encodedPayload, encodedSignature, found := strings.Cut(token, ".")
	if !found {
		return fmt.Errorf("%w: malformed", ErrInvalidCursor)
	}
	signature, err := base64.RawURLEncoding.DecodeString(encodedSignature)
	if err != nil || !hmac.Equal(signature, codec.sign(encodedPayload)) {
		return fmt.Errorf("%w: bad signature", ErrInvalidCursor)
	}
	payloadBytes, err := base64.RawURLEncoding.DecodeString(encodedPayload)
	if err != nil {
		return fmt.Errorf("%w: malformed payload", ErrInvalidCursor)
	}
	var payload cursorPayload
	if err := json.Unmarshal(payloadBytes, &payload); err != nil {
		return fmt.Errorf("%w: malformed payload", ErrInvalidCursor)
	}
	if payload.Version != cursorVersion || payload.Scope != scope {
		return fmt.Errorf("%w: issued for another version or collection", ErrInvalidCursor)
	}
	if err := json.Unmarshal(payload.Position, target); err != nil {
		return fmt.Errorf("%w: malformed position", ErrInvalidCursor)
	}
	return nil
}

func (codec *CursorCodec) sign(encodedPayload string) []byte {
	mac := hmac.New(sha256.New, codec.secret)
	mac.Write([]byte(encodedPayload))
	return mac.Sum(nil)
}
