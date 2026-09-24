package telemetry

import (
	"fmt"

	"go.opentelemetry.io/otel/attribute"
)

// With builds one attribute.KeyValue from key and value, inferring the
// OpenTelemetry attribute type from the concrete type of value: string,
// int, int64, float64 and bool map to their matching attribute
// constructor, a fmt.Stringer is rendered through its String method, and
// any other type falls back to a string built with fmt.Sprint.
func With(key string, value any) attribute.KeyValue {
	switch typedValue := value.(type) {
	case string:
		return attribute.String(key, typedValue)
	case int:
		return attribute.Int(key, typedValue)
	case int64:
		return attribute.Int64(key, typedValue)
	case float64:
		return attribute.Float64(key, typedValue)
	case bool:
		return attribute.Bool(key, typedValue)
	case fmt.Stringer:
		return attribute.String(key, typedValue.String())
	default:
		return attribute.String(key, fmt.Sprint(value))
	}
}
