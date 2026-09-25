package cache

import (
	"context"
	"errors"
	"fmt"
	"reflect"
	"strconv"
	"strings"
)

// keyPrefix starts every storage key.
const keyPrefix = "frappe:"

// ErrMissingTenant is returned by Invalidate and InvalidateFor on a
// tenant-scoped entry when no tenant is known. Get never returns it: it
// skips the cache and loads instead.
var ErrMissingTenant = errors.New("cache: tenant-scoped entry used without a tenant")

// errInvalidKey is returned when a key encodes to an empty string.
var errInvalidKey = errors.New("cache: key encodes to an empty string")

// Key constrains the key type of a ReadThrough. Supported key types are
// checked when New is called: a type implementing KeyEncoder, a
// fmt.Stringer (such as uuid.UUID), a string kind or an integer kind.
// Anything else (floats, structs without CacheKey, pointers) panics in New.
type Key interface {
	comparable
}

// KeyEncoder lets a composite key type choose its own encoding, for
// example "region/slug". The result is escaped like every other key, so it
// can never inject a separator.
type KeyEncoder interface {
	CacheKey() string
}

var (
	keyEncoderType = reflect.TypeFor[KeyEncoder]()
	stringerType   = reflect.TypeFor[fmt.Stringer]()
)

// escaper removes the key separator from free text; escaping the escape
// character too keeps the encoding injective.
var escaper = strings.NewReplacer("%", "%25", ":", "%3A")

func escape(text string) string {
	return escaper.Replace(text)
}

// supportedKeyType reports whether encodeKey can encode values of kind.
func supportedKeyType(kind reflect.Type) bool {
	if kind.Implements(keyEncoderType) || kind.Implements(stringerType) {
		return true
	}
	switch kind.Kind() {
	case reflect.String,
		reflect.Int, reflect.Int8, reflect.Int16, reflect.Int32, reflect.Int64,
		reflect.Uint, reflect.Uint8, reflect.Uint16, reflect.Uint32, reflect.Uint64:
		return true
	default:
		return false
	}
}

// encodeKey deterministically encodes value, escaped, reporting false for
// an empty encoding.
func encodeKey(value reflect.Value) (string, bool) {
	text := keyText(value)
	return escape(text), text != ""
}

func keyText(value reflect.Value) string {
	switch concrete := value.Interface().(type) {
	case KeyEncoder:
		return concrete.CacheKey()
	case fmt.Stringer:
		return concrete.String()
	}
	switch value.Kind() {
	case reflect.String:
		return value.String()
	case reflect.Int, reflect.Int8, reflect.Int16, reflect.Int32, reflect.Int64:
		return strconv.FormatInt(value.Int(), 10)
	default:
		return strconv.FormatUint(value.Uint(), 10)
	}
}

// scopeSegment returns "global" for global entries and "tenant:<tenant>"
// otherwise.
func scopeSegment(global bool, tenant string) (string, error) {
	if global {
		if tenant != "" {
			return "", errors.New("cache: a global entry does not take a tenant")
		}
		return "global", nil
	}
	if tenant == "" {
		return "", ErrMissingTenant
	}
	return "tenant:" + escape(tenant), nil
}

// tenantFrom resolves the tenant of ctx, or "" when unknown.
func tenantFrom(ctx context.Context, resolver TenantResolver) string {
	if resolver == nil {
		return ""
	}
	tenant, found := resolver(ctx)
	if !found {
		return ""
	}
	return tenant
}
