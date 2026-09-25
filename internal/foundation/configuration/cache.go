package configuration

// Cache stores selectable with CACHE_STORE.
const (
	CacheStoreMemory = "memory"
	CacheStoreValkey = "valkey"
)

// validateCache checks the CACHE_* settings, and that Valkey is configured
// for the valkey store, only while caching is enabled.
func validateCache(cache Cache, valkey Valkey) []Violation {
	if !cache.Enabled {
		return nil
	}
	var violations []Violation
	switch cache.Store {
	case CacheStoreMemory:
	case CacheStoreValkey:
		if valkey.Address == "" {
			violations = append(violations, Violation{Variable: "VALKEY_ADDRESS", Rule: "required_when_cache_store_valkey"})
		}
	default:
		violations = append(violations, Violation{Variable: "CACHE_STORE", Rule: "oneof=memory valkey"})
	}
	if cache.DefaultTimeToLive <= 0 {
		violations = append(violations, Violation{Variable: "CACHE_DEFAULT_TIME_TO_LIVE", Rule: "gt"})
	}
	if cache.OperationTimeout <= 0 {
		violations = append(violations, Violation{Variable: "CACHE_OPERATION_TIMEOUT", Rule: "gt"})
	}
	return violations
}
