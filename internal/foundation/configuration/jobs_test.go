package configuration_test

import (
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
)

func jobsEnabledConfiguration() configuration.Configuration {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.Jobs = configuration.Jobs{
		Enabled:            true,
		Workers:            10,
		MaxAttempts:        25,
		FetchPollInterval:  time.Second,
		JobTimeout:         time.Minute,
		CompletedRetention: 24 * time.Hour,
		MetricsInterval:    15 * time.Second,
	}
	return loadedConfiguration
}

func TestValidateAcceptsEnabledJobs(t *testing.T) {
	if err := configuration.Validate(jobsEnabledConfiguration()); err != nil {
		t.Fatalf("Validate returned unexpected error: %v", err)
	}
}

func TestValidateIgnoresJobsSettingsWhileDisabled(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.Jobs = configuration.Jobs{Workers: -1}
	if err := configuration.Validate(loadedConfiguration); err != nil {
		t.Fatalf("Validate returned unexpected error: %v", err)
	}
}

func TestValidateRejectsInvalidJobsSettings(t *testing.T) {
	cases := map[string]func(*configuration.Configuration){
		"JOBS_WORKERS":             func(loaded *configuration.Configuration) { loaded.Jobs.Workers = 0 },
		"JOBS_MAX_ATTEMPTS":        func(loaded *configuration.Configuration) { loaded.Jobs.MaxAttempts = 0 },
		"JOBS_FETCH_POLL_INTERVAL": func(loaded *configuration.Configuration) { loaded.Jobs.FetchPollInterval = 0 },
		"JOBS_JOB_TIMEOUT":         func(loaded *configuration.Configuration) { loaded.Jobs.JobTimeout = 0 },
		"JOBS_COMPLETED_RETENTION": func(loaded *configuration.Configuration) { loaded.Jobs.CompletedRetention = 0 },
		"JOBS_METRICS_INTERVAL":    func(loaded *configuration.Configuration) { loaded.Jobs.MetricsInterval = 0 },
	}
	for variable, mutate := range cases {
		t.Run(variable, func(t *testing.T) {
			loadedConfiguration := jobsEnabledConfiguration()
			mutate(&loadedConfiguration)
			assertViolation(t, loadedConfiguration, variable)
		})
	}
}
