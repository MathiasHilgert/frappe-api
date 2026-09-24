package health

import (
	"fmt"
	"time"
)

// Default values applied by NewChecker when Config leaves a field at its
// zero value.
const (
	DefaultInterval         = 10 * time.Second
	DefaultTimeout          = 2 * time.Second
	DefaultFailureThreshold = 3
)

// Config configures a Checker's background run loop.
type Config struct {
	// Interval is how often every registered check is run in the
	// background. Defaults to DefaultInterval.
	Interval time.Duration
	// Timeout bounds how long a single check's Run call may take.
	// Defaults to DefaultTimeout.
	Timeout time.Duration
	// FailureThreshold is how many consecutive failures a check must
	// accumulate before it is marked failing. A single success
	// immediately restores it. Defaults to DefaultFailureThreshold.
	FailureThreshold int
}

// withDefaults returns config with every zero-valued field replaced by its
// package default.
func (config Config) withDefaults() Config {
	if config.Interval <= 0 {
		config.Interval = DefaultInterval
	}
	if config.Timeout <= 0 {
		config.Timeout = DefaultTimeout
	}
	if config.FailureThreshold <= 0 {
		config.FailureThreshold = DefaultFailureThreshold
	}
	return config
}

// Validate checks that config's explicitly set fields (any left at zero
// are filled by defaults instead of rejected) are not negative.
func (config Config) Validate() error {
	if config.Interval < 0 {
		return fmt.Errorf("health: interval must not be negative, got %s", config.Interval)
	}
	if config.Timeout < 0 {
		return fmt.Errorf("health: timeout must not be negative, got %s", config.Timeout)
	}
	if config.FailureThreshold < 0 {
		return fmt.Errorf("health: failure threshold must not be negative, got %d", config.FailureThreshold)
	}
	return nil
}
