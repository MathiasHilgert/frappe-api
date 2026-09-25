package cache

import "encoding/json"

// ValueCodec turns cached values into bytes and back. The default is
// JSONCodec; select another one per entry with the Codec option.
type ValueCodec interface {
	Encode(value any) ([]byte, error)
	Decode(data []byte, target any) error
}

// JSONCodec encodes values with encoding/json. Only exported fields
// survive a round trip, so cached types must be JSON friendly.
type JSONCodec struct{}

// Encode implements ValueCodec.
func (JSONCodec) Encode(value any) ([]byte, error) {
	return json.Marshal(value)
}

// Decode implements ValueCodec.
func (JSONCodec) Decode(data []byte, target any) error {
	return json.Unmarshal(data, target)
}
