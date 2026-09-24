package health

import (
	"fmt"
	"time"
)

// Default values applied by NewChecker when Settings leaves a field at its
// zero value.
const (
	DefaultInterval         = 10 * time.Second
	DefaultTimeout          = 2 * time.Second
	DefaultFailureThreshold = 3
)

// Settings configures a Checker's background run loop.
type Settings struct {
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

// withDefaults returns settings with every zero-valued field replaced by its
// package default.
func (settings Settings) withDefaults() Settings {
	if settings.Interval <= 0 {
		settings.Interval = DefaultInterval
	}
	if settings.Timeout <= 0 {
		settings.Timeout = DefaultTimeout
	}
	if settings.FailureThreshold <= 0 {
		settings.FailureThreshold = DefaultFailureThreshold
	}
	return settings
}

// Validate checks that settings's explicitly set fields (any left at zero
// are filled by defaults instead of rejected) are not negative.
func (settings Settings) Validate() error {
	if settings.Interval < 0 {
		return fmt.Errorf("health: interval must not be negative, got %s", settings.Interval)
	}
	if settings.Timeout < 0 {
		return fmt.Errorf("health: timeout must not be negative, got %s", settings.Timeout)
	}
	if settings.FailureThreshold < 0 {
		return fmt.Errorf("health: failure threshold must not be negative, got %d", settings.FailureThreshold)
	}
	return nil
}
