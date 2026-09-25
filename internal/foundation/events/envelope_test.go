package events_test

import (
	"context"
	"encoding/json"
	"errors"
	"testing"
	"time"

	"go.opentelemetry.io/otel/trace"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
)

var envelopeCreated = events.Define[orderCreated]("envelopetest.created", 1)

func TestEnvelopeRoundTrip(t *testing.T) {
	event := envelopeCreated.With(orderCreated{OrderID: "order-7", Total: 99}).ForEntity("order-7", 4)
	event.TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"
	event.TraceState = "vendor=value"

	message, err := events.NewMessage(context.Background(), event)
	if err != nil {
		t.Fatalf("NewMessage: %v", err)
	}
	if message.ID != event.ID || message.Subject != "frappe.envelopetest.created.v1" {
		t.Fatalf("unexpected message identity: %+v", message)
	}
	if got := message.Headers["content-type"]; got != "application/cloudevents+json" {
		t.Fatalf("content-type header = %q", got)
	}

	decoded, err := envelopeCreated.Decode(message.Payload)
	if err != nil {
		t.Fatalf("Decode: %v", err)
	}
	if !decoded.Time.Equal(event.Time) {
		t.Fatalf("time = %v, want %v", decoded.Time, event.Time)
	}
	decoded.Time = event.Time
	if decoded != event {
		t.Fatalf("round trip mismatch:\n got %+v\nwant %+v", decoded, event)
	}
}

func TestEnvelopeWireFormat(t *testing.T) {
	event := envelopeCreated.With(orderCreated{OrderID: "order-1", Total: 5}).ForEntity("order-1", 2)
	message, err := events.NewMessage(context.Background(), event)
	if err != nil {
		t.Fatalf("NewMessage: %v", err)
	}

	var wire map[string]any
	if err := json.Unmarshal(message.Payload, &wire); err != nil {
		t.Fatalf("payload is not JSON: %v", err)
	}
	expected := map[string]any{
		"specversion":     "1.0",
		"id":              event.ID,
		"source":          "frappe-api/envelopetest",
		"type":            "frappe.envelopetest.created.v1",
		"datacontenttype": "application/json",
		"partitionkey":    "order-1",
		"sequence":        float64(2),
	}
	for key, value := range expected {
		if wire[key] != value {
			t.Errorf("%s = %v, want %v", key, wire[key], value)
		}
	}
	if _, err := time.Parse(time.RFC3339Nano, wire["time"].(string)); err != nil {
		t.Errorf("time is not RFC 3339: %v", wire["time"])
	}
	data, ok := wire["data"].(map[string]any)
	if !ok || data["orderId"] != "order-1" {
		t.Errorf("data = %v", wire["data"])
	}
	for _, absent := range []string{"traceparent", "tracestate"} {
		if _, present := wire[absent]; present {
			t.Errorf("%s must be omitted without a span in context", absent)
		}
	}
}

func TestEnvelopeOmitsEntityExtensionsWhenUnscoped(t *testing.T) {
	message, err := events.NewMessage(context.Background(), envelopeCreated.With(orderCreated{}))
	if err != nil {
		t.Fatalf("NewMessage: %v", err)
	}
	var wire map[string]any
	if err := json.Unmarshal(message.Payload, &wire); err != nil {
		t.Fatal(err)
	}
	for _, absent := range []string{"partitionkey", "sequence"} {
		if _, present := wire[absent]; present {
			t.Errorf("%s must be omitted for an unscoped event", absent)
		}
	}
}

func TestNewMessageCapturesTraceContext(t *testing.T) {
	traceID, _ := trace.TraceIDFromHex("0af7651916cd43dd8448eb211c80319c")
	spanID, _ := trace.SpanIDFromHex("b7ad6b7169203331")
	state, _ := trace.ParseTraceState("vendor=value")
	spanContext := trace.NewSpanContext(trace.SpanContextConfig{
		TraceID: traceID, SpanID: spanID, TraceFlags: trace.FlagsSampled, TraceState: state,
	})
	ctx := trace.ContextWithSpanContext(context.Background(), spanContext)

	message, err := events.NewMessage(ctx, envelopeCreated.With(orderCreated{}))
	if err != nil {
		t.Fatalf("NewMessage: %v", err)
	}
	decoded, err := envelopeCreated.Decode(message.Payload)
	if err != nil {
		t.Fatalf("Decode: %v", err)
	}
	if decoded.TraceParent != "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01" {
		t.Fatalf("TraceParent = %q", decoded.TraceParent)
	}
	if decoded.TraceState != "vendor=value" {
		t.Fatalf("TraceState = %q", decoded.TraceState)
	}
	if message.Headers["traceparent"] != decoded.TraceParent {
		t.Fatalf("traceparent header = %q", message.Headers["traceparent"])
	}
}

func TestNewMessageRejectsInvalidEntityScope(t *testing.T) {
	cases := map[string]events.Event[orderCreated]{
		"sequence without key": envelopeCreated.With(orderCreated{}).ForEntity("", 1),
		"key without sequence": envelopeCreated.With(orderCreated{}).ForEntity("order-1", 0),
		"negative sequence":    envelopeCreated.With(orderCreated{}).ForEntity("order-1", -1),
	}
	for name, event := range cases {
		t.Run(name, func(t *testing.T) {
			if _, err := events.NewMessage(context.Background(), event); err == nil {
				t.Fatal("expected an error")
			}
		})
	}
}

func TestDecodeRejectsInvalidEnvelopes(t *testing.T) {
	valid := `"specversion":"1.0","id":"id-1","source":"frappe-api/envelopetest","type":"frappe.envelopetest.created.v1","time":"2026-01-02T03:04:05Z","datacontenttype":"application/json"`
	cases := map[string]string{
		"not json":                 `nope`,
		"trailing data":            `{` + valid + `,"data":{}} {}`,
		"wrong spec version":       `{"specversion":"0.3","id":"id-1","source":"s","type":"frappe.envelopetest.created.v1","datacontenttype":"application/json","data":{}}`,
		"missing id":               `{"specversion":"1.0","source":"s","type":"frappe.envelopetest.created.v1","datacontenttype":"application/json","data":{}}`,
		"missing source":           `{"specversion":"1.0","id":"id-1","type":"frappe.envelopetest.created.v1","datacontenttype":"application/json","data":{}}`,
		"missing type":             `{"specversion":"1.0","id":"id-1","source":"s","datacontenttype":"application/json","data":{}}`,
		"wrong content type":       `{"specversion":"1.0","id":"id-1","source":"s","type":"frappe.envelopetest.created.v1","datacontenttype":"text/plain","data":{}}`,
		"invalid time":             `{"specversion":"1.0","id":"id-1","source":"s","type":"frappe.envelopetest.created.v1","time":"yesterday","datacontenttype":"application/json","data":{}}`,
		"missing data":             `{` + valid + `}`,
		"sequence not an integer":  `{` + valid + `,"partitionkey":"k","sequence":"one","data":{}}`,
		"sequence without key":     `{` + valid + `,"sequence":1,"data":{}}`,
		"other event type":         `{"specversion":"1.0","id":"id-1","source":"s","type":"frappe.other.created.v1","datacontenttype":"application/json","data":{}}`,
		"data of the wrong shape":  `{` + valid + `,"data":{"orderId":7}}`,
		"null payload":             `null`,
		"spec version wrong type":  `{"specversion":1,"id":"id-1","source":"s","type":"frappe.envelopetest.created.v1","datacontenttype":"application/json","data":{}}`,
		"trace parent wrong type":  `{` + valid + `,"traceparent":5,"data":{}}`,
		"partition key wrong type": `{` + valid + `,"partitionkey":5,"sequence":1,"data":{}}`,
	}
	for name, payload := range cases {
		t.Run(name, func(t *testing.T) {
			_, err := envelopeCreated.Decode([]byte(payload))
			if err == nil {
				t.Fatal("expected an error")
			}
			if !errors.Is(err, events.ErrInvalidEnvelope) {
				t.Fatalf("error %v does not wrap ErrInvalidEnvelope", err)
			}
		})
	}
}

func TestDecodeAcceptsUnknownExtensions(t *testing.T) {
	payload := `{"specversion":"1.0","id":"id-1","source":"frappe-api/envelopetest","type":"frappe.envelopetest.created.v1","time":"2026-01-02T03:04:05Z","datacontenttype":"application/json","customextension":"x","data":{"orderId":"o","total":1,"addedLater":true}}`
	event, err := envelopeCreated.Decode([]byte(payload))
	if err != nil {
		t.Fatalf("Decode: %v", err)
	}
	if event.ID != "id-1" || event.Data.OrderID != "o" || event.Time.Year() != 2026 {
		t.Fatalf("unexpected event %+v", event)
	}
}
