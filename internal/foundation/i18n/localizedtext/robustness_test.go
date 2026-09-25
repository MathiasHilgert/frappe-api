package localizedtext_test

import (
	"context"
	"errors"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/localizedtext"
)

type failingRequester struct{}

func (failingRequester) RequestTranslations(context.Context, []localizedtext.TranslationRequest) error {
	return errors.New("outbox unavailable")
}

func TestReadsSurviveAFailingRequester(t *testing.T) {
	t.Parallel()
	text := sourceText()
	store := &fakeStore{texts: map[localizedtext.ID]localizedtext.Text{text.ID: text}}
	service, err := localizedtext.NewService(localizedtext.Settings{Store: store, Locales: testCatalog(t), Requester: failingRequester{}})
	if err != nil {
		t.Fatalf("NewService: %v", err)
	}
	fieldTexts, err := service.Field(dishDescription)
	if err != nil {
		t.Fatalf("Field: %v", err)
	}
	localized, err := fieldTexts.LocalizeMany(context.Background(), []localizedtext.ID{text.ID}, english)
	if err != nil || localized[text.ID].Value != text.SourceValue {
		t.Fatalf("LocalizeMany with a failing requester = %+v, %v; want the source value and no error", localized, err)
	}
}

func TestLocalizeRerequestsExpiredPendingTranslations(t *testing.T) {
	t.Parallel()
	now := time.Date(2026, 9, 25, 12, 0, 0, 0, time.UTC)
	fresh := sourceText(localizedtext.Translation{Locale: english, Origin: localizedtext.OriginMachine, Status: localizedtext.StatusPending, RequestedAt: now.Add(-time.Minute)})
	expired := sourceText(localizedtext.Translation{Locale: english, Origin: localizedtext.OriginMachine, Status: localizedtext.StatusPending, RequestedAt: now.Add(-time.Hour)})
	store := &fakeStore{texts: map[localizedtext.ID]localizedtext.Text{fresh.ID: fresh, expired.ID: expired}}
	requester := &recordingRequester{}
	service, err := localizedtext.NewService(localizedtext.Settings{
		Store: store, Locales: testCatalog(t), Requester: requester,
		PendingTimeout: 15 * time.Minute, Now: func() time.Time { return now },
	})
	if err != nil {
		t.Fatalf("NewService: %v", err)
	}
	fieldTexts, err := service.Field(dishDescription)
	if err != nil {
		t.Fatalf("Field: %v", err)
	}
	if _, err := fieldTexts.LocalizeMany(context.Background(), []localizedtext.ID{fresh.ID, expired.ID}, english); err != nil {
		t.Fatalf("LocalizeMany: %v", err)
	}
	if len(requester.requests) != 1 || requester.requests[0].TextID != expired.ID {
		t.Fatalf("requests = %+v, want only the expired pending translation", requester.requests)
	}
}

func TestDeleteOrphansValidatesForeignKeysAgainstDeclaredFields(t *testing.T) {
	t.Parallel()
	declared := localizedtext.Reference{Table: "dishes", Column: "description_text_id", OnDelete: localizedtext.OnDeleteNoAction}
	cases := map[string]struct {
		want       error
		references []localizedtext.Reference
	}{
		"undeclared foreign key": {
			references: []localizedtext.Reference{declared, {Table: "menus", Column: "title_text_id", OnDelete: localizedtext.OnDeleteRestrict}},
			want:       localizedtext.ErrUndeclaredReference,
		},
		"declared field without a foreign key": {want: localizedtext.ErrMissingForeignKey},
		"cascading foreign key": {
			references: []localizedtext.Reference{{Table: "dishes", Column: "description_text_id", OnDelete: "cascade"}},
			want:       localizedtext.ErrUnsafeForeignKey,
		},
		"consistent": {references: []localizedtext.Reference{declared}},
	}
	for name, testCase := range cases {
		t.Run(name, func(t *testing.T) {
			t.Parallel()
			store := &fakeStore{references: testCase.references}
			service, err := localizedtext.NewService(localizedtext.Settings{Store: store, Locales: testCatalog(t)})
			if err != nil {
				t.Fatalf("NewService: %v", err)
			}
			if _, fieldError := service.Field(dishDescription); fieldError != nil {
				t.Fatalf("Field: %v", fieldError)
			}
			_, err = service.DeleteOrphans(context.Background(), 0, 10)
			if testCase.want == nil {
				if err != nil || len(store.swept) != 1 {
					t.Fatalf("DeleteOrphans = %v, swept %+v; want the declared reference swept", err, store.swept)
				}
				return
			}
			if !errors.Is(err, testCase.want) {
				t.Fatalf("DeleteOrphans error = %v, want %v", err, testCase.want)
			}
		})
	}
}
