package nats

import (
	"errors"
	"strings"
	"time"
)

// Stream and subject layout provisioned by Up.
const (
	// StreamName is the stream holding every published event.
	StreamName = "FRAPPE_EVENTS"
	// StreamSubjects captures every event type (frappe.<module>.<event>.v<n>).
	StreamSubjects = "frappe.>"
	// DeadLetterStreamName is the stream holding dead letters.
	DeadLetterStreamName = "FRAPPE_EVENTS_DEAD_LETTER"
	// DeadLetterSubjectPrefix roots dead letter subjects. It is deliberately
	// not under "frappe." so StreamSubjects never captures a dead letter.
	DeadLetterSubjectPrefix = "frappe-dead-letter."
)

// Default values applied by Up, after Validate, to zero-valued fields.
const (
	DefaultName             = "frappe-api"
	DefaultConnectTimeout   = 5 * time.Second
	DefaultReconnectWait    = 2 * time.Second
	DefaultReplicas         = 1
	DefaultMaxAge           = 7 * 24 * time.Hour
	DefaultDuplicateWindow  = 2 * time.Minute
	DefaultDeadLetterMaxAge = 30 * 24 * time.Hour
	DefaultAckWait          = 30 * time.Second
	// maximumReplicas is the JetStream limit on stream replicas.
	maximumReplicas = 5
)

// Settings configures the connection and the streams provisioned by Up.
type Settings struct {
	// URL lists one or more comma-separated server URLs. Required. A tls://
	// scheme enables TLS.
	URL string
	// Name identifies the connection on the server. Defaults to DefaultName.
	Name string
	// Token authenticates with a token. Secret. Exclusive with User and
	// CredentialsFile.
	Token string
	// User and Password authenticate with a user and password. Password is a
	// secret. Exclusive with Token and CredentialsFile.
	User     string
	Password string
	// CredentialsFile is the path of a JWT/NKey credentials file (a
	// decentralized-auth .creds file). Exclusive with Token and User.
	CredentialsFile string
	// TLSCertificateAuthorityFile is an optional PEM file of root
	// certificates used to verify the server; setting it enables TLS.
	TLSCertificateAuthorityFile string
	// ConnectTimeout bounds establishing the connection. Defaults to
	// DefaultConnectTimeout.
	ConnectTimeout time.Duration
	// Replicas is the replica count of both streams (1 to 5). Defaults to
	// DefaultReplicas.
	Replicas int
	// MaxAge is how long the events stream retains messages. Defaults to
	// DefaultMaxAge.
	MaxAge time.Duration
	// DuplicateWindow is how long the events stream remembers message IDs to
	// drop redundant publishes. Defaults to DefaultDuplicateWindow.
	DuplicateWindow time.Duration
	// DeadLetterMaxAge is how long dead letters are retained. Defaults to
	// DefaultDeadLetterMaxAge.
	DeadLetterMaxAge time.Duration
	// AckWait is how long a delivery may stay unacknowledged (for example
	// because the process died mid-handler) before JetStream redelivers it.
	// Defaults to DefaultAckWait.
	AckWait time.Duration
}

// Validate checks the raw settings, before defaults are applied.
func (settings Settings) Validate() error {
	if strings.TrimSpace(settings.URL) == "" {
		return errors.New("nats: url must not be empty")
	}
	if err := settings.validateAuthentication(); err != nil {
		return err
	}
	for _, duration := range []time.Duration{settings.ConnectTimeout, settings.MaxAge, settings.DuplicateWindow, settings.DeadLetterMaxAge, settings.AckWait} {
		if duration < 0 {
			return errors.New("nats: durations must not be negative")
		}
	}
	if settings.Replicas < 0 || settings.Replicas > maximumReplicas {
		return errors.New("nats: replicas must be between 1 and 5")
	}
	if settings.MaxAge > 0 && settings.DuplicateWindow > settings.MaxAge {
		return errors.New("nats: duplicate window must not exceed max age")
	}
	return nil
}

func (settings Settings) validateAuthentication() error {
	if settings.Password != "" && settings.User == "" {
		return errors.New("nats: password requires a user")
	}
	methods := 0
	for _, configured := range []bool{settings.Token != "", settings.User != "", settings.CredentialsFile != ""} {
		if configured {
			methods++
		}
	}
	if methods > 1 {
		return errors.New("nats: token, user/password and credentials file are mutually exclusive")
	}
	return nil
}

func (settings Settings) withDefaults() Settings {
	if settings.Name == "" {
		settings.Name = DefaultName
	}
	settings.ConnectTimeout = orDefault(settings.ConnectTimeout, DefaultConnectTimeout)
	settings.MaxAge = orDefault(settings.MaxAge, DefaultMaxAge)
	settings.DuplicateWindow = orDefault(settings.DuplicateWindow, min(DefaultDuplicateWindow, settings.MaxAge))
	settings.DeadLetterMaxAge = orDefault(settings.DeadLetterMaxAge, DefaultDeadLetterMaxAge)
	settings.AckWait = orDefault(settings.AckWait, DefaultAckWait)
	if settings.Replicas == 0 {
		settings.Replicas = DefaultReplicas
	}
	return settings
}

func orDefault(value, fallback time.Duration) time.Duration {
	if value == 0 {
		return fallback
	}
	return value
}

// ConsumerName turns a subscription name into a valid JetStream durable
// consumer name by replacing every character other than ASCII letters,
// digits, '-' and '_' with '_'.
func ConsumerName(subscription string) string {
	return strings.Map(func(character rune) rune {
		switch {
		case character >= 'a' && character <= 'z', character >= 'A' && character <= 'Z',
			character >= '0' && character <= '9', character == '-', character == '_':
			return character
		default:
			return '_'
		}
	}, subscription)
}

// AckTimeoutBackoff builds the JetStream consumer BackOff list from a
// subscription's backoff. JetStream applies BackOff only to deliveries that
// time out unacknowledged (it replaces AckWait), never to naks, so every
// interval is raised to at least ackWait: a short retry delay must not cause
// a still-running handler's message to be redelivered concurrently. The list
// is truncated to maxDeliver entries, as JetStream requires.
func AckTimeoutBackoff(backoff []time.Duration, ackWait time.Duration, maxDeliver int) []time.Duration {
	if len(backoff) == 0 {
		return nil
	}
	intervals := make([]time.Duration, 0, min(len(backoff), maxDeliver))
	for _, delay := range backoff[:min(len(backoff), maxDeliver)] {
		intervals = append(intervals, max(delay, ackWait))
	}
	return intervals
}
