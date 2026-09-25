package memory_test

import (
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/inbox"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/inbox/inboxtest"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/inbox/memory"
)

func TestStoreContract(t *testing.T) {
	inboxtest.Run(t, func(*testing.T) inbox.Store { return memory.NewStore() })
}
