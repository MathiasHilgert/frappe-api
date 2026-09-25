package configuration_test

import (
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
)

func cachedConfiguration(store string) configuration.Configuration {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.Cache = configuration.Cache{
		Enabled: true, Store: store, DefaultTimeToLive: 5 * time.Minute, OperationTimeout: 100 * time.Millisecond,
	}
	loadedConfiguration.Valkey = configuration.Valkey{Address: "localhost:6379", DialTimeout: 5 * time.Second, WriteTimeout: 5 * time.Second}
	return loadedConfiguration
}

func TestValidateAcceptsEnabledCacheStores(t *testing.T) {
	for _, store := range []string{configuration.CacheStoreMemory, configuration.CacheStoreValkey} {
		if err := configuration.Validate(cachedConfiguration(store)); err != nil {
			t.Fatalf("Validate with CACHE_STORE=%s returned unexpected error: %v", store, err)
		}
	}
}

func TestValidateIgnoresADisabledCache(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.Cache = configuration.Cache{Store: "bogus"}
	if err := configuration.Validate(loadedConfiguration); err != nil {
		t.Fatalf("Validate returned unexpected error: %v", err)
	}
}

func TestValidateRejectsInvalidCacheSettings(t *testing.T) {
	loadedConfiguration := cachedConfiguration("bogus")
	assertViolation(t, loadedConfiguration, "CACHE_STORE")

	loadedConfiguration = cachedConfiguration(configuration.CacheStoreValkey)
	loadedConfiguration.Valkey.Address = ""
	assertViolation(t, loadedConfiguration, "VALKEY_ADDRESS")

	loadedConfiguration = cachedConfiguration(configuration.CacheStoreMemory)
	loadedConfiguration.Cache.DefaultTimeToLive = 0
	assertViolation(t, loadedConfiguration, "CACHE_DEFAULT_TIME_TO_LIVE")

	loadedConfiguration = cachedConfiguration(configuration.CacheStoreMemory)
	loadedConfiguration.Cache.OperationTimeout = 0
	assertViolation(t, loadedConfiguration, "CACHE_OPERATION_TIMEOUT")
}

func TestValidateAcceptsAMemoryCacheWithoutValkey(t *testing.T) {
	loadedConfiguration := cachedConfiguration(configuration.CacheStoreMemory)
	loadedConfiguration.Valkey = configuration.Valkey{}
	if err := configuration.Validate(loadedConfiguration); err != nil {
		t.Fatalf("Validate returned unexpected error: %v", err)
	}
}
