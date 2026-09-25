package dependencies

import (
	"crypto/rand"
	"fmt"
	"log/slog"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/rest"
)

// provideCursorCodec builds the *rest.CursorCodec every module with
// paginated collections receives through its Dependencies. It signs with
// HTTP_CURSOR_SECRET and also accepts cursors signed with
// HTTP_CURSOR_PREVIOUS_SECRETS (rotation); when the secret is empty (only allowed in development,
// see configuration.Validate) it uses a random per-process secret, so
// cursors stop working after a restart and across replicas.
func provideCursorCodec(settings configuration.HTTP) (*rest.CursorCodec, error) {
	secret := []byte(settings.CursorSecret)
	if len(secret) == 0 {
		secret = make([]byte, rest.MinimumCursorSecretBytes)
		// crypto/rand.Read never returns an error (Go 1.24+); it aborts
		// the process if the system random source fails.
		_, _ = rand.Read(secret)
		slog.Warn("HTTP_CURSOR_SECRET is empty: pagination cursors use a random per-process secret and do not survive a restart")
	}
	previous := make([][]byte, 0, len(settings.CursorPreviousSecrets))
	for _, value := range settings.CursorPreviousSecrets {
		previous = append(previous, []byte(value))
	}
	codec, err := rest.NewCursorCodec(secret, previous...)
	if err != nil {
		return nil, fmt.Errorf("cursor codec: %w", err)
	}
	return codec, nil
}
