package query

// distinct is a list of lookup keys that may repeat.
type distinct[T comparable] []T

// values returns the keys without duplicates, in first occurrence order.
func (keys distinct[T]) values() []T {
	seen := make(map[T]struct{}, len(keys))
	result := make([]T, 0, len(keys))
	for _, key := range keys {
		if _, found := seen[key]; !found {
			seen[key] = struct{}{}
			result = append(result, key)
		}
	}
	return result
}
