package outbox

import (
	"context"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
)

// Recorder is the production events.Recorder: it encodes each event as a
// CloudEvents envelope, capturing the trace context of ctx, and appends it to
// the Store in the caller's unit of work. It knows neither the broker nor the
// storage technology.
type Recorder struct {
	store Store
}

// NewRecorder returns a Recorder appending to store.
func NewRecorder(store Store) *Recorder {
	return &Recorder{store: store}
}

// Record implements events.Recorder. It must be called with the context of
// the unit of work that performs the business change, so that the event is
// stored if and only if that change commits.
func (recorder *Recorder) Record(ctx context.Context, event events.Recordable) error {
	message, err := events.NewMessage(ctx, event)
	if err != nil {
		return err
	}
	return recorder.store.Append(ctx, message)
}
