package query

import (
	"context"
	"sync"

	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
)

// GetDataRevision asks for the revision of the loaded geo snapshot, which
// HTTP responses derive their ETags from.
type GetDataRevision struct{}

// GetDataRevisionHandler reads the revision once and then serves it from
// memory: it only changes with a migration, which comes with a deploy and
// a restart. A failed read is not kept, so the next call retries. The
// module loads it at startup, so requests never wait for it.
type GetDataRevisionHandler struct {
	revisions application.DataRevisionReader
	revision  string
	mutex     sync.Mutex
	loaded    bool
}

// NewGetDataRevisionHandler returns a GetDataRevisionHandler reading
// revisions.
func NewGetDataRevisionHandler(revisions application.DataRevisionReader) *GetDataRevisionHandler {
	return &GetDataRevisionHandler{revisions: revisions}
}

// Handle returns the revision.
func (handler *GetDataRevisionHandler) Handle(ctx context.Context, _ GetDataRevision) (string, error) {
	handler.mutex.Lock()
	defer handler.mutex.Unlock()
	if handler.loaded {
		return handler.revision, nil
	}
	revision, err := handler.revisions.DataRevision(ctx)
	if err != nil {
		return "", err
	}
	handler.revision, handler.loaded = revision, true
	return revision, nil
}
