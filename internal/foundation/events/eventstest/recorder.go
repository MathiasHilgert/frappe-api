// Package eventstest provides test doubles for the events package.
//
// Use cases record events through an events.Recorder; in unit tests inject a
// *Recorder and assert with Recorded:
//
//	recorder := eventstest.NewRecorder()
//	useCase := application.NewPlaceOrder(application.Dependencies{Events: recorder})
//	...
//	created := eventstest.Recorded(t, recorder, orderevents.Created)
package eventstest

import (
	"context"
	"sync"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
)

// Recorder is an in-memory events.Recorder capturing every recorded event.
// It is safe for concurrent use.
type Recorder struct {
	failure  error
	recorded []events.Recordable
	mutex    sync.Mutex
}

// NewRecorder returns an empty Recorder.
func NewRecorder() *Recorder {
	return &Recorder{}
}

// Record captures event. It applies the same encoding validation as the
// production recorder, so an event that could not be stored fails here too.
func (recorder *Recorder) Record(ctx context.Context, event events.Recordable) error {
	if _, err := events.NewMessage(ctx, event); err != nil {
		return err
	}
	recorder.mutex.Lock()
	defer recorder.mutex.Unlock()
	if recorder.failure != nil {
		return recorder.failure
	}
	recorder.recorded = append(recorder.recorded, event)
	return nil
}

// Fail makes every following Record return err without capturing the event,
// to exercise failure paths. Fail(nil) restores normal behavior.
func (recorder *Recorder) Fail(err error) {
	recorder.mutex.Lock()
	defer recorder.mutex.Unlock()
	recorder.failure = err
}

// All returns every captured event in recording order.
func (recorder *Recorder) All() []events.Recordable {
	recorder.mutex.Lock()
	defer recorder.mutex.Unlock()
	return append([]events.Recordable(nil), recorder.recorded...)
}

// Reset forgets every captured event.
func (recorder *Recorder) Reset() {
	recorder.mutex.Lock()
	defer recorder.mutex.Unlock()
	recorder.recorded = nil
}

// Recorded returns, in recording order, the captured events of definition,
// typed so assertions can read their Data directly.
func Recorded[T any](t testing.TB, recorder *Recorder, definition events.Definition[T]) []events.Event[T] {
	t.Helper()
	var matching []events.Event[T]
	for _, recordable := range recorder.All() {
		event, ok := recordable.(events.Event[T])
		if ok && event.Type == definition.Type() {
			matching = append(matching, event)
		}
	}
	return matching
}
