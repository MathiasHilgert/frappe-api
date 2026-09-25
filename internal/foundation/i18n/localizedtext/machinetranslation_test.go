package localizedtext_test

import (
	"context"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/localizedtext"
)

// sweepStore adds the sweeper's Store methods to fakeStore.
type sweepStore struct {
	leasedUntil time.Time
	*fakeStore
	referencing map[localizedtext.ID]localizedtext.Reference
	expired     []localizedtext.TranslationRequest
	tenants     []string
	failed      []localizedtext.ID
	leased      []localizedtext.ID
}

func (store *sweepStore) MarkFailed(_ context.Context, _ i18n.Locale, ids []localizedtext.ID) error {
	store.failed = append(store.failed, ids...)
	return nil
}

func (store *sweepStore) Lease(_ context.Context, _ i18n.Locale, ids []localizedtext.ID, until time.Time) error {
	store.leased = append(store.leased, ids...)
	store.leasedUntil = until
	return nil
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

func TestRequestExpiredGivesUpAfterMaxRequestAttempts(t *testing.T) {
	t.Parallel()
	exhausted := localizedtext.TranslationRequest{TextID: localizedtext.NewID(), Locale: english, Context: "hint", Attempts: 3}
	retried := localizedtext.TranslationRequest{TextID: localizedtext.NewID(), Locale: english, Context: "hint", Attempts: 2}
	store := &sweepStore{fakeStore: &fakeStore{}, expired: []localizedtext.TranslationRequest{exhausted, retried}}
	requester := &recordingRequester{}
	service, err := localizedtext.NewService(localizedtext.Settings{
		Store: store, Locales: testCatalog(t), Requester: requester, MaxRequestAttempts: 3,
	})
	if err != nil {
		t.Fatalf("NewService: %v", err)
	}
	requested, err := service.RequestExpired(context.Background(), 10)
	if err != nil || requested != 1 {
		t.Fatalf("RequestExpired = %d, %v; want 1, nil", requested, err)
	}
	if len(store.failed) != 1 || store.failed[0] != exhausted.TextID {
		t.Fatalf("failed = %v, want only the exhausted text", store.failed)
	}
	if len(requester.requests) != 1 || requester.requests[0].TextID != retried.TextID {
		t.Fatalf("requested = %+v, want only the retried text", requester.requests)
	}
}

func TestLeaseAndMarkFailedReachTheStore(t *testing.T) {
	t.Parallel()
	store := &sweepStore{fakeStore: &fakeStore{}}
	service, err := localizedtext.NewService(localizedtext.Settings{Store: store, Locales: testCatalog(t)})
	if err != nil {
		t.Fatalf("NewService: %v", err)
	}
	id := localizedtext.NewID()
	until := time.Date(2026, 9, 25, 13, 0, 0, 0, time.UTC)
	if err := service.LeasePending(context.Background(), english, []localizedtext.ID{id}, until); err != nil {
		t.Fatal(err)
	}
	if err := service.MarkFailed(context.Background(), english, []localizedtext.ID{id}); err != nil {
		t.Fatal(err)
	}
	if len(store.leased) != 1 || !store.leasedUntil.Equal(until) || len(store.failed) != 1 {
		t.Fatalf("leased %v until %s, failed %v", store.leased, store.leasedUntil, store.failed)
	}
}

func TestFailedTranslationsAreNotRequestedOnRead(t *testing.T) {
	t.Parallel()
	failed := sourceText(localizedtext.Translation{Locale: english, Origin: localizedtext.OriginMachine, Status: localizedtext.StatusFailed})
	tested := newFixture(t, failed)
	localized, err := tested.texts.Localize(context.Background(), failed.ID, english)
	if err != nil || !localized.Fallback {
		t.Fatalf("Localize = %+v, %v; want the source as fallback", localized, err)
	}
	if len(tested.requester.requests) != 0 {
		t.Fatalf("requests = %+v, want none for a failed translation", tested.requester.requests)
	}
}
