package telemetry

import (
	"fmt"
	"testing"

	"go.opentelemetry.io/otel/attribute"
)

type stringerValue struct{ text string }

func (value stringerValue) String() string { return value.text }

func TestWithInfersAttributeType(t *testing.T) {
	tests := []struct {
		value    any
		expected attribute.KeyValue
		name     string
		key      string
	}{
		{name: "string", key: "order.status", value: "paid", expected: attribute.String("order.status", "paid")},
		{name: "int", key: "order.items", value: 3, expected: attribute.Int("order.items", 3)},
		{name: "int64", key: "order.items64", value: int64(3), expected: attribute.Int64("order.items64", 3)},
		{name: "float64", key: "order.total", value: 12.5, expected: attribute.Float64("order.total", 12.5)},
		{name: "bool", key: "order.paid", value: true, expected: attribute.Bool("order.paid", true)},
		{name: "stringer", key: "order.id", value: stringerValue{"abc"}, expected: attribute.String("order.id", "abc")},
		{name: "fallback", key: "order.other", value: []int{1, 2}, expected: attribute.String("order.other", fmt.Sprint([]int{1, 2}))},
	}

	for _, testCase := range tests {
		t.Run(testCase.name, func(t *testing.T) {
			got := With(testCase.key, testCase.value)
			if got != testCase.expected {
				t.Errorf("With(%q, %v) = %#v, want %#v", testCase.key, testCase.value, got, testCase.expected)
			}
		})
	}
}
