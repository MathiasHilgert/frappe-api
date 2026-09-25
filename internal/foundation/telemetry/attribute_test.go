package telemetry

import (
	"fmt"
	"math"
	"strconv"
	"testing"
	"time"

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
		{name: "int8", key: "order.retries", value: int8(3), expected: attribute.Int("order.retries", 3)},
		{name: "int16", key: "order.retries16", value: int16(3), expected: attribute.Int("order.retries16", 3)},
		{name: "int32", key: "order.retries32", value: int32(3), expected: attribute.Int("order.retries32", 3)},
		{name: "uint", key: "order.count", value: uint(3), expected: attribute.Int64("order.count", 3)},
		{name: "uint8", key: "order.count8", value: uint8(3), expected: attribute.Int64("order.count8", 3)},
		{name: "uint16", key: "order.count16", value: uint16(3), expected: attribute.Int64("order.count16", 3)},
		{name: "uint32", key: "order.count32", value: uint32(3), expected: attribute.Int64("order.count32", 3)},
		{name: "uint64 within int64 range", key: "order.count64", value: uint64(3), expected: attribute.Int64("order.count64", 3)},
		{
			name:     "uint64 above math.MaxInt64",
			key:      "order.hugeCount",
			value:    uint64(math.MaxInt64) + 1,
			expected: attribute.String("order.hugeCount", strconv.FormatUint(uint64(math.MaxInt64)+1, 10)),
		},
		{name: "float32", key: "order.weight", value: float32(1.5), expected: attribute.Float64("order.weight", float64(float32(1.5)))},
		{name: "duration as seconds", key: "order.latency", value: 1500 * time.Millisecond, expected: attribute.Float64("order.latency", 1.5)},
		{name: "string slice", key: "order.tags", value: []string{"a", "b"}, expected: attribute.StringSlice("order.tags", []string{"a", "b"})},
		{name: "int slice", key: "order.quantities", value: []int{1, 2, 3}, expected: attribute.IntSlice("order.quantities", []int{1, 2, 3})},
		{name: "int64 slice", key: "order.quantities64", value: []int64{1, 2, 3}, expected: attribute.Int64Slice("order.quantities64", []int64{1, 2, 3})},
		{name: "float64 slice", key: "order.weights", value: []float64{1.5, 2.5}, expected: attribute.Float64Slice("order.weights", []float64{1.5, 2.5})},
		{name: "bool slice", key: "order.flags", value: []bool{true, false}, expected: attribute.BoolSlice("order.flags", []bool{true, false})},
		{name: "fallback", key: "order.other", value: []int32{1, 2}, expected: attribute.String("order.other", fmt.Sprint([]int32{1, 2}))},
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
