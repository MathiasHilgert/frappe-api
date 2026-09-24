package application

import (
	"log/slog"
	"os"
	"time"
)

// defaultHookTimeout bounds how long a single hook's Up or Down call may run
// when no explicit timeout is configured.
const defaultHookTimeout = 30 * time.Second

// buildInfo holds the running build's identity, recorded once as the
// frappe.application.info gauge when set through WithBuildInfo.
type buildInfo struct {
	version     string
	commit      string
	environment string
	set         bool
}

// options holds the configuration assembled by functional Option values.
type options struct {
	signals     []os.Signal
	logger      *slog.Logger
	buildInfo   buildInfo
	hookTimeout time.Duration
}

// Option configures an Application created with New.
type Option func(*options)

// WithHookTimeout sets the maximum duration allowed for each individual
// hook's Up or Down call. Every hook runs under its own context derived
// from this timeout, so one slow hook cannot block the others indefinitely.
func WithHookTimeout(timeout time.Duration) Option {
	return func(o *options) {
		o.hookTimeout = timeout
	}
}

// WithLogger sets the logger used to record hook start and stop events.
func WithLogger(logger *slog.Logger) Option {
	return func(o *options) {
		o.logger = logger
	}
}

// WithSignals sets the operating system signals that Run treats as a
// shutdown request. When omitted, Run defaults to SIGINT and SIGTERM.
func WithSignals(signals ...os.Signal) Option {
	return func(o *options) {
		o.signals = signals
	}
}

// WithBuildInfo records the running build's identity as the
// frappe.application.info gauge, with version, commit and environment
// attributes. Callers typically pass internal/foundation/build.Version
// and internal/foundation/build.Commit together with the deployment
// environment from configuration.
func WithBuildInfo(version, commit, environment string) Option {
	return func(o *options) {
		o.buildInfo = buildInfo{version: version, commit: commit, environment: environment, set: true}
	}
}

// newOptions builds the effective options for an Application from the given
// functional Option values, applying defaults for anything left unset.
func newOptions(optionFunctions []Option) options {
	resolved := options{
		hookTimeout: defaultHookTimeout,
		logger:      slog.Default(),
	}
	for _, apply := range optionFunctions {
		apply(&resolved)
	}
	return resolved
}
