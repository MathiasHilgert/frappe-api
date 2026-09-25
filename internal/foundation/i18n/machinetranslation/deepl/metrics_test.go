package deepl_test

import (
	"context"
	"errors"
	"net/http"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/machinetranslation"
)

func TestCharactersSentCarryTheRequestOutcome(t *testing.T) {
	success := map[string]string{"target": "KO", "outcome": "success"}
	failure := map[string]string{"target": "KO", "outcome": "server_error"}
	successBefore := counterValue(t, "frappe.deepl.characters.sent", success)
	failureBefore := counterValue(t, "frappe.deepl.characters.sent", failure)
	failing, _ := newServer(t, http.StatusServiceUnavailable, nil, ``)
	if _, err := newClient(t, failing.URL).Translate(context.Background(), request("es-419", "ko", "abcd")); err == nil {
		t.Fatal("want a failure")
	}
	working, _ := newServer(t, http.StatusOK, nil, `{"translations":[{"text":"x"}]}`)
	if _, err := newClient(t, working.URL).Translate(context.Background(), request("es-419", "ko", "abcdef")); err != nil {
		t.Fatal(err)
	}
	if got := counterValue(t, "frappe.deepl.characters.sent", success) - successBefore; got != 6 {
		t.Fatalf("successful characters = %d, want 6", got)
	}
	if got := counterValue(t, "frappe.deepl.characters.sent", failure) - failureBefore; got != 4 {
		t.Fatalf("failed characters = %d, want 4", got)
	}
}

func TestPayloadTooLargeIsReportedAsSuch(t *testing.T) {
	server, _ := newServer(t, http.StatusRequestEntityTooLarge, nil, `{"message":"too large"}`)
	_, err := newClient(t, server.URL).Translate(context.Background(), request("es-419", "en", "hola"))
	if !errors.Is(err, machinetranslation.ErrPayloadTooLarge) {
		t.Fatalf("err = %v, want ErrPayloadTooLarge", err)
	}
}
