package cache

import (
	"reflect"
	"testing"

	"github.com/google/uuid"
)

type region string

type pair struct{ left, right string }

func (value pair) CacheKey() string { return escape(value.left) + ":" + escape(value.right) }

func TestEncodedKeysNeverCollide(t *testing.T) {
	encoded := map[string]any{}
	values := []any{
		"a", "a:b", "a%3Ab", "a%b", ":", "%", "%3A", "7", region("7"),
		int(7), int64(-7), uint8(7), uuid.Nil,
		pair{"a", "b:c"},
		pair{"a:b", "c"},
	}
	for _, value := range values {
		key, ok := encodeKey(reflect.ValueOf(value))
		if !ok {
			t.Fatalf("encodeKey(%#v) was rejected", value)
		}
		if previous, exists := encoded[key]; exists && !sameText(previous, value) {
			t.Fatalf("%#v and %#v both encode to %q", previous, value, key)
		}
		encoded[key] = value
	}
}

// sameText reports whether two values are the same key text in different
// Go types (for example "7", region("7") and 7), which intentionally share
// one encoding: within one ReadThrough the key type is fixed.
func sameText(left, right any) bool {
	leftKey, _ := encodeKey(reflect.ValueOf(left))
	rightKey, _ := encodeKey(reflect.ValueOf(right))
	return leftKey == rightKey && reflect.TypeOf(left) != reflect.TypeOf(right)
}

func TestEscapeRemovesSeparators(t *testing.T) {
	for input, want := range map[string]string{"a:b": "a%3Ab", "50%": "50%25", "plain": "plain"} {
		if got := escape(input); got != want {
			t.Fatalf("escape(%q) = %q; want %q", input, got, want)
		}
	}
}
