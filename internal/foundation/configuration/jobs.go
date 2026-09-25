package configuration

import "time"

// Jobs holds settings for background and periodic jobs (see
// internal/foundation/jobs and its River backend). Jobs are stored in the
// application database, so they need no other infrastructure. While
// disabled, this replica still enqueues jobs (they wait in the database)
// but neither works them nor takes part in the periodic leader election.
// The remaining fields are only validated while enabled.
type Jobs struct {
	// FetchPollInterval is how often each queue polls for jobs when no
	// notification arrives.
	FetchPollInterval time.Duration `env:"FETCH_POLL_INTERVAL" envDefault:"1s"`
	// JobTimeout bounds one attempt of a job whose definition sets none.
	JobTimeout time.Duration `env:"JOB_TIMEOUT" envDefault:"1m"`
	// CompletedRetention is how long completed jobs are kept.
	CompletedRetention time.Duration `env:"COMPLETED_RETENTION" envDefault:"24h"`
	// MetricsInterval is how often queue depth and leadership are sampled.
	MetricsInterval time.Duration `env:"METRICS_INTERVAL" envDefault:"15s"`
	// Workers is the maximum number of concurrent attempts per queue.
	Workers int `env:"WORKERS" envDefault:"10"`
	// MaxAttempts applies to jobs whose definition sets none.
	MaxAttempts int `env:"MAX_ATTEMPTS" envDefault:"25"`
	// Enabled works jobs on this replica.
	Enabled bool `env:"ENABLED" envDefault:"true"`
}

// validateJobs checks the JOBS_* settings while jobs are enabled.
func validateJobs(jobs Jobs) []Violation {
	if !jobs.Enabled {
		return nil
	}
	var violations []Violation
	if jobs.Workers < 1 {
		violations = append(violations, Violation{Variable: "JOBS_WORKERS", Rule: "min"})
	}
	if jobs.MaxAttempts < 1 {
		violations = append(violations, Violation{Variable: "JOBS_MAX_ATTEMPTS", Rule: "min"})
	}
	durations := []struct {
		variable string
		value    time.Duration
	}{
		{"JOBS_FETCH_POLL_INTERVAL", jobs.FetchPollInterval},
		{"JOBS_JOB_TIMEOUT", jobs.JobTimeout},
		{"JOBS_COMPLETED_RETENTION", jobs.CompletedRetention},
		{"JOBS_METRICS_INTERVAL", jobs.MetricsInterval},
	}
	for _, duration := range durations {
		if duration.value <= 0 {
			violations = append(violations, Violation{Variable: duration.variable, Rule: "gt"})
		}
	}
	return violations
}
