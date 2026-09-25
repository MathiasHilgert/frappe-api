package database

import (
	"testing"
	"time"
)

func TestPoolConfigurationKeepsAnExplicitZeroMinConnections(t *testing.T) {
	poolConfig, err := poolConfiguration(Settings{URL: "postgres://localhost/frappe", MinConnections: 0})
	if err != nil {
		t.Fatalf("poolConfiguration returned unexpected error: %v", err)
	}
	if poolConfig.MinConns != 0 {
		t.Fatalf("MinConns = %d, want 0 (zero is a valid minimum, not an unset field)", poolConfig.MinConns)
	}
}

func TestPoolConfigurationAppliesDefaultsAfterValidation(t *testing.T) {
	poolConfig, err := poolConfiguration(Settings{URL: "postgres://localhost/frappe"})
	if err != nil {
		t.Fatalf("poolConfiguration returned unexpected error: %v", err)
	}
	if poolConfig.MaxConns != DefaultMaxConnections {
		t.Errorf("MaxConns = %d, want %d", poolConfig.MaxConns, DefaultMaxConnections)
	}
	if poolConfig.MaxConnLifetime != DefaultMaxConnectionLifetime {
		t.Errorf("MaxConnLifetime = %s, want %s", poolConfig.MaxConnLifetime, DefaultMaxConnectionLifetime)
	}
	if poolConfig.MaxConnIdleTime != DefaultMaxConnectionIdleTime {
		t.Errorf("MaxConnIdleTime = %s, want %s", poolConfig.MaxConnIdleTime, DefaultMaxConnectionIdleTime)
	}
	if poolConfig.ConnConfig.ConnectTimeout != DefaultConnectTimeout {
		t.Errorf("ConnectTimeout = %s, want %s", poolConfig.ConnConfig.ConnectTimeout, DefaultConnectTimeout)
	}
}

func TestPoolConfigurationAppliesAnExplicitConnectTimeout(t *testing.T) {
	poolConfig, err := poolConfiguration(Settings{URL: "postgres://localhost/frappe", ConnectTimeout: 2 * time.Second})
	if err != nil {
		t.Fatalf("poolConfiguration returned unexpected error: %v", err)
	}
	if poolConfig.ConnConfig.ConnectTimeout != 2*time.Second {
		t.Fatalf("ConnectTimeout = %s, want 2s", poolConfig.ConnConfig.ConnectTimeout)
	}
}

func TestPoolConfigurationRejectsMinConnectionsAboveTheDefaultMax(t *testing.T) {
	_, err := poolConfiguration(Settings{URL: "postgres://localhost/frappe", MinConnections: DefaultMaxConnections + 1})
	if err == nil {
		t.Fatal("poolConfiguration returned nil error for MinConnections above the default MaxConnections")
	}
}
