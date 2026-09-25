package configuration

import "time"

// Event broker implementations selectable with EVENTS_BROKER.
const (
	// EventsBrokerNone wires no broker; nothing is published or consumed.
	EventsBrokerNone = "none"
	// EventsBrokerMemory wires the in-process broker (single process only,
	// messages are lost on restart).
	EventsBrokerMemory = "memory"
	// EventsBrokerNATS wires NATS JetStream; NATS_URL is required.
	EventsBrokerNATS = "nats"
)

// maximumStreamReplicas is the JetStream limit on stream replicas.
const maximumStreamReplicas = 5

// Events holds settings for the event platform (see
// internal/foundation/events).
type Events struct {
	// Broker selects the Publisher/Subscriber implementation: none (the
	// default, so a plain local run needs no broker), memory or nats. An
	// empty value means none.
	Broker string `env:"BROKER" envDefault:"none" validate:"omitempty,oneof=none memory nats"`
}

// NATS holds settings for the NATS JetStream connection (see
// internal/foundation/nats). It is only connected while EVENTS_BROKER=nats.
type NATS struct {
	// URL lists comma-separated server URLs; tls:// enables TLS.
	URL string `env:"URL"`
	// Token authenticates with a token. Secret.
	Token string `env:"TOKEN"`
	// User and Password authenticate with a user and password. Password is
	// a secret.
	User     string `env:"USER"`
	Password string `env:"PASSWORD"`
	// CredentialsFile is the path of a JWT/NKey .creds file.
	CredentialsFile string `env:"CREDENTIALS_FILE"`
	// TLSCertificateAuthorityFile is an optional PEM file of root
	// certificates; setting it enables TLS.
	TLSCertificateAuthorityFile string `env:"TLS_CERTIFICATE_AUTHORITY_FILE"`
	// ConnectTimeout bounds establishing the connection.
	ConnectTimeout time.Duration `env:"CONNECT_TIMEOUT" envDefault:"5s" validate:"min=0"`
	// StreamMaxAge is how long FRAPPE_EVENTS retains messages.
	StreamMaxAge time.Duration `env:"STREAM_MAX_AGE" envDefault:"168h" validate:"min=0"`
	// StreamDuplicateWindow is how long published message IDs are
	// remembered to drop redundant publishes.
	StreamDuplicateWindow time.Duration `env:"STREAM_DUPLICATE_WINDOW" envDefault:"2m" validate:"min=0"`
	// DeadLetterMaxAge is how long dead letters are retained.
	DeadLetterMaxAge time.Duration `env:"DEAD_LETTER_MAX_AGE" envDefault:"720h" validate:"min=0"`
	// AckWait is how long a delivery may stay unacknowledged before
	// JetStream redelivers it.
	AckWait time.Duration `env:"ACK_WAIT" envDefault:"30s" validate:"min=0"`
	// StreamReplicas is the replica count of both streams (1 to 5).
	StreamReplicas int `env:"STREAM_REPLICAS" envDefault:"1" validate:"min=0"`
}

// brokerSelected reports whether EVENTS_BROKER selects a broker.
func (events Events) brokerSelected() bool {
	return events.Broker != "" && events.Broker != EventsBrokerNone
}

// validateInbox checks the INBOX_* settings only while a broker is selected.
func validateInbox(inbox Inbox, events Events) []Violation {
	if !events.brokerSelected() {
		return nil
	}
	var violations []Violation
	if inbox.PurgeInterval <= 0 {
		violations = append(violations, Violation{Variable: "INBOX_PURGE_INTERVAL", Rule: "gt"})
	}
	if inbox.Retention <= 0 {
		violations = append(violations, Violation{Variable: "INBOX_RETENTION", Rule: "gt"})
	}
	return violations
}

// validateEvents checks the NATS settings only while NATS is selected.
func validateEvents(events Events, nats NATS) []Violation {
	if events.Broker != EventsBrokerNATS {
		return nil
	}
	var violations []Violation
	if nats.URL == "" {
		violations = append(violations, Violation{Variable: "NATS_URL", Rule: "required_when_events_broker_nats"})
	}
	if nats.StreamReplicas > maximumStreamReplicas {
		violations = append(violations, Violation{Variable: "NATS_STREAM_REPLICAS", Rule: "max"})
	}
	return violations
}
