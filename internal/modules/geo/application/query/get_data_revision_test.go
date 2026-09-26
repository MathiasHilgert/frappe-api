package query_test

import (
	"context"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application/query"
)

func TestGetDataRevisionReadsOnceAndRetriesFailures(t *testing.T) {
	t.Parallel()
	readers := newFakeReaders()
	readers.revisionError = errDatabase
	handler := query.NewGetDataRevisionHandler(readers)

	if _, err := handler.Handle(context.Background(), query.GetDataRevision{}); err == nil {
		t.Fatalf("error = nil, want the read error")
	}
	readers.revisionError, readers.revision = nil, "geo-seed-3"
	for range 3 {
		if revision, err := handler.Handle(context.Background(), query.GetDataRevision{}); err != nil || revision != "geo-seed-3" {
			t.Fatalf("Handle = %q, %v", revision, err)
		}
	}
	if readers.calls["DataRevision"] != 2 {
		t.Fatalf("read %d times, want 2 (one failure, then kept in memory)", readers.calls["DataRevision"])
	}
}
