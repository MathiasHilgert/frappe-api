package localizedtext_test

import (
	"context"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/localizedtext"
)

// sweepStore adds the sweeper's Store methods to fakeStore.
type sweepStore struct {
	*fakeStore
	referencing map[localizedtext.ID]localizedtext.Reference
	expired     []localizedtext.TranslationRequest
	tenants     []string
}

func (store *sweepStore) ExpiredPending(context.Context, time.Duration, int) ([]localizedtext.TranslationRequest, error) {
	return store.expired, nil
}

func (store *sweepStore) Referencing(_ context.Context, _ []localizedtext.Reference, ids []localizedtext.ID) (map[localizedtext.ID]localizedtext.Reference, error) {
	found := map[localizedtext.ID]localizedtext.Reference{}
	for _, id := range ids {
		if reference, ok := store.referencing[id]; ok {
			found[id] = reference
		}
	}
	return found, nil
}

func (store *sweepStore) Tenants(context.Context) ([]string, error) {
	return store.tenants, nil
}

func TestRequestExpiredRequestsAgainWithTheFieldDefaultContext(t *testing.T) {
	t.Parallel()
	own := localizedtext.TranslationRequest{TextID: localizedtext.NewID(), Locale: english, Context: "Own hint", Reason: localizedtext.ReasonExpired}
	defaulted := localizedtext.TranslationRequest{TextID: localizedtext.NewID(), Locale: portuguese, Reason: localizedtext.ReasonExpired}
	unreferenced := localizedtext.TranslationRequest{TextID: localizedtext.NewID(), Locale: english, Reason: localizedtext.ReasonExpired}
	store := &sweepStore{
		fakeStore:   &fakeStore{texts: map[localizedtext.ID]localizedtext.Text{}},
		expired:     []localizedtext.TranslationRequest{own, defaulted, unreferenced},
		referencing: map[localizedtext.ID]localizedtext.Reference{defaulted.TextID: {Table: "dishes", Column: "description_text_id"}},
	}
	requester := &recordingRequester{}
	service, err := localizedtext.NewService(localizedtext.Settings{Store: store, Locales: testCatalog(t), Requester: requester})
	if err != nil {
		t.Fatalf("NewService: %v", err)
	}
	if _, err = service.Field(dishDescription); err != nil {
		t.Fatalf("Field: %v", err)
	}
	requested, err := service.RequestExpired(context.Background(), 10)
	if err != nil || requested != 3 {
		t.Fatalf("RequestExpired = %d, %v; want 3, nil", requested, err)
	}
	contexts := map[localizedtext.ID]string{}
	for _, request := range requester.requests {
		contexts[request.TextID] = request.Context
	}
	want := map[localizedtext.ID]string{own.TextID: "Own hint", defaulted.TextID: dishDescription.Context, unreferenced.TextID: ""}
	for id, hint := range want {
		if got, ok := contexts[id]; !ok || got != hint {
			t.Errorf("context of %s = %q (requested %v), want %q", id, got, ok, hint)
		}
	}
	if len(store.pending) != 3 {
		t.Errorf("marked pending = %d, want 3 (attempts and requested_at advance)", len(store.pending))
	}
}

func TestRequestExpiredIsANoOpWithoutARequester(t *testing.T) {
	t.Parallel()
	store := &sweepStore{fakeStore: &fakeStore{}, expired: []localizedtext.TranslationRequest{{TextID: localizedtext.NewID(), Locale: english}}}
	service, err := localizedtext.NewService(localizedtext.Settings{Store: store, Locales: testCatalog(t)})
	if err != nil {
		t.Fatalf("NewService: %v", err)
	}
	if requested, err := service.RequestExpired(context.Background(), 10); err != nil || requested != 0 {
		t.Fatalf("RequestExpired = %d, %v; want 0, nil", requested, err)
	}
}

func TestTenantsListsTheStoreTenants(t *testing.T) {
	t.Parallel()
	store := &sweepStore{fakeStore: &fakeStore{}, tenants: []string{"acme", "globex"}}
	service, err := localizedtext.NewService(localizedtext.Settings{Store: store, Locales: testCatalog(t)})
	if err != nil {
		t.Fatalf("NewService: %v", err)
	}
	tenants, err := service.Tenants(context.Background())
	if err != nil || len(tenants) != 2 {
		t.Fatalf("Tenants = %v, %v", tenants, err)
	}
}
