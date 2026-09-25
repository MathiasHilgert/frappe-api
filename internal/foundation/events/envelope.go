package events

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"time"

	"go.opentelemetry.io/otel/propagation"
)

// Wire constants of the CloudEvents 1.0 structured JSON mode.
const (
	specVersion         = "1.0"
	dataContentType     = "application/json"
	envelopeContentType = "application/cloudevents+json"
)

// Header names set on every Message built by NewMessage. Adapters map them onto
// their transport headers so brokers can route and trace without decoding
// the payload.
const (
	HeaderContentType = "content-type"
	HeaderTraceParent = "traceparent"
	HeaderTraceState  = "tracestate"
)

// ErrInvalidEnvelope is wrapped by every decoding error: the payload is not a
// valid CloudEvents 1.0 structured JSON envelope for the expected type.
var ErrInvalidEnvelope = errors.New("events: invalid envelope")

// Recordable is an event that can be recorded and published. Event[T]
// implements it; use Definition.With to build one.
type Recordable interface {
	// Envelope returns the event as a CloudEvents envelope.
	Envelope() (Envelope, error)
}

// Envelope is the broker-independent CloudEvents 1.0 representation of an
// event. Data holds the JSON-encoded event payload.
type Envelope struct {
	Time         time.Time
	ID           string
	Source       string
	Type         string
	PartitionKey string
	TraceParent  string
	TraceState   string
	Data         json.RawMessage
	Sequence     int64
}

// wireEnvelope is the JSON shape of an Envelope. Pointers distinguish absent
// attributes from empty ones.
type wireEnvelope struct {
	SpecVersion     *string         `json:"specversion"`
	ID              *string         `json:"id"`
	Source          *string         `json:"source"`
	Type            *string         `json:"type"`
	Time            *string         `json:"time,omitempty"`
	DataContentType *string         `json:"datacontenttype"`
	PartitionKey    *string         `json:"partitionkey,omitempty"`
	Sequence        *int64          `json:"sequence,omitempty"`
	TraceParent     *string         `json:"traceparent,omitempty"`
	TraceState      *string         `json:"tracestate,omitempty"`
	Data            json.RawMessage `json:"data"`
}

// Envelope implements Recordable.
func (event Event[T]) Envelope() (Envelope, error) {
	data, err := json.Marshal(event.Data)
	if err != nil {
		return Envelope{}, fmt.Errorf("events: encode data of %s: %w", event.Type, err)
	}
	return Envelope{
		ID:           event.ID,
		Source:       event.Source,
		Type:         event.Type,
		Time:         event.Time,
		PartitionKey: event.EntityKey,
		Sequence:     event.Sequence,
		TraceParent:  event.TraceParent,
		TraceState:   event.TraceState,
		Data:         data,
	}, nil
}

// validate checks the invariants shared by encoding and decoding.
func (envelope Envelope) validate() error {
	switch {
	case envelope.ID == "":
		return errors.New("id is required")
	case envelope.Source == "":
		return errors.New("source is required")
	case envelope.Type == "":
		return errors.New("type is required")
	case len(envelope.Data) == 0:
		return errors.New("data is required")
	case envelope.Sequence < 0:
		return errors.New("sequence must not be negative")
	case (envelope.PartitionKey == "") != (envelope.Sequence == 0):
		return errors.New("partitionkey and sequence must be set together")
	}
	return nil
}

// MarshalJSON encodes the envelope in CloudEvents 1.0 structured JSON mode.
func (envelope Envelope) MarshalJSON() ([]byte, error) {
	if err := envelope.validate(); err != nil {
		return nil, fmt.Errorf("%w: %w", ErrInvalidEnvelope, err)
	}
	wire := wireEnvelope{
		SpecVersion:     pointer(specVersion),
		ID:              pointer(envelope.ID),
		Source:          pointer(envelope.Source),
		Type:            pointer(envelope.Type),
		DataContentType: pointer(dataContentType),
		PartitionKey:    optional(envelope.PartitionKey),
		TraceParent:     optional(envelope.TraceParent),
		TraceState:      optional(envelope.TraceState),
		Data:            envelope.Data,
	}
	if !envelope.Time.IsZero() {
		wire.Time = pointer(envelope.Time.UTC().Format(time.RFC3339Nano))
	}
	if envelope.Sequence != 0 {
		wire.Sequence = pointer(envelope.Sequence)
	}
	return json.Marshal(wire)
}

// DecodeEnvelope decodes a CloudEvents 1.0 structured JSON envelope. Unknown
// extension attributes are ignored; every error wraps ErrInvalidEnvelope.
func DecodeEnvelope(payload []byte) (Envelope, error) {
	envelope, err := decodeEnvelope(payload)
	if err != nil {
		return Envelope{}, fmt.Errorf("%w: %w", ErrInvalidEnvelope, err)
	}
	return envelope, nil
}

func decodeEnvelope(payload []byte) (Envelope, error) {
	var wire wireEnvelope
	decoder := json.NewDecoder(bytes.NewReader(payload))
	if err := decoder.Decode(&wire); err != nil {
		return Envelope{}, err
	}
	if _, err := decoder.Token(); !errors.Is(err, io.EOF) {
		return Envelope{}, errors.New("unexpected data after the envelope")
	}
	if value(wire.SpecVersion) != specVersion {
		return Envelope{}, fmt.Errorf("specversion must be %q", specVersion)
	}
	if value(wire.DataContentType) != dataContentType {
		return Envelope{}, fmt.Errorf("datacontenttype must be %q", dataContentType)
	}
	envelope := Envelope{
		ID:           value(wire.ID),
		Source:       value(wire.Source),
		Type:         value(wire.Type),
		PartitionKey: value(wire.PartitionKey),
		Sequence:     value(wire.Sequence),
		TraceParent:  value(wire.TraceParent),
		TraceState:   value(wire.TraceState),
		Data:         wire.Data,
	}
	if wire.Time != nil {
		parsed, err := time.Parse(time.RFC3339Nano, *wire.Time)
		if err != nil {
			return Envelope{}, fmt.Errorf("time: %w", err)
		}
		envelope.Time = parsed.UTC()
	}
	return envelope, envelope.validate()
}

// Decode decodes payload into an event of this definition. It fails with an
// error wrapping ErrInvalidEnvelope when the envelope is invalid, carries a
// different type, or its data does not match T. Unknown data fields are
// ignored so producers can add fields without a new version.
func (definition Definition[T]) Decode(payload []byte) (Event[T], error) {
	envelope, err := DecodeEnvelope(payload)
	if err != nil {
		return Event[T]{}, err
	}
	if envelope.Type != definition.eventType {
		return Event[T]{}, fmt.Errorf("%w: type %q, want %q", ErrInvalidEnvelope, envelope.Type, definition.eventType)
	}
	var data T
	if err := json.Unmarshal(envelope.Data, &data); err != nil {
		return Event[T]{}, fmt.Errorf("%w: data of %s: %w", ErrInvalidEnvelope, envelope.Type, err)
	}
	return Event[T]{
		ID:          envelope.ID,
		Type:        envelope.Type,
		Source:      envelope.Source,
		Time:        envelope.Time,
		EntityKey:   envelope.PartitionKey,
		Sequence:    envelope.Sequence,
		TraceParent: envelope.TraceParent,
		TraceState:  envelope.TraceState,
		Data:        data,
	}, nil
}

// NewMessage encodes event as a CloudEvents envelope ready to be stored or
// published. When the event carries no trace context, the span context of
// ctx (if any) is captured so consumers continue the producer's trace. The
// message subject is the event type.
func NewMessage(ctx context.Context, event Recordable) (Message, error) {
	envelope, err := event.Envelope()
	if err != nil {
		return Message{}, err
	}
	if envelope.TraceParent == "" {
		carrier := propagation.MapCarrier{}
		propagation.TraceContext{}.Inject(ctx, carrier)
		envelope.TraceParent = carrier.Get(HeaderTraceParent)
		envelope.TraceState = carrier.Get(HeaderTraceState)
	}
	payload, err := json.Marshal(envelope)
	if err != nil {
		return Message{}, err
	}
	headers := map[string]string{HeaderContentType: envelopeContentType}
	if envelope.TraceParent != "" {
		headers[HeaderTraceParent] = envelope.TraceParent
	}
	if envelope.TraceState != "" {
		headers[HeaderTraceState] = envelope.TraceState
	}
	return Message{ID: envelope.ID, Subject: envelope.Type, Payload: payload, Headers: headers}, nil
}

func pointer[T any](input T) *T {
	return &input
}

func optional(input string) *string {
	if input == "" {
		return nil
	}
	return &input
}

func value[T any](input *T) T {
	var zero T
	if input == nil {
		return zero
	}
	return *input
}
