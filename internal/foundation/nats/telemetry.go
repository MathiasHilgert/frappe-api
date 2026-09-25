package nats

import (
	"fmt"
	"sync"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/metric"
)

// instrumentationScope names the meter used by this package.
const instrumentationScope = "github.com/MathiasHilgert/frappe-api/internal/foundation/nats"

// Outcomes recorded on frappe.nats.publish.duration.
const (
	outcomeSuccess = "success"
	outcomeFailure = "failure"
)

type natsInstruments struct {
	reconnects      metric.Int64Counter
	publishDuration metric.Float64Histogram
}

// instruments are created lazily through the global OpenTelemetry API, so
// they follow whatever meter provider the telemetry foundation installs.
var instruments = sync.OnceValue(func() natsInstruments {
	meter := otel.Meter(instrumentationScope)
	reconnects, err := meter.Int64Counter("frappe.nats.reconnects",
		metric.WithDescription("Reconnections of the NATS connection after it was lost."),
		metric.WithUnit("{reconnect}"))
	if err != nil {
		panic(fmt.Errorf("nats: create reconnects counter: %w", err))
	}
	publishDuration, err := meter.Float64Histogram("frappe.nats.publish.duration",
		metric.WithDescription("Duration of JetStream publishes until the server acknowledged them, by stream and outcome."),
		metric.WithUnit("s"))
	if err != nil {
		panic(fmt.Errorf("nats: create publish duration histogram: %w", err))
	}
	return natsInstruments{reconnects: reconnects, publishDuration: publishDuration}
})
