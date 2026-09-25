package machinetranslation_test

import (
	"errors"
	"slices"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/localizedtext"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/machinetranslation"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/jobs"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/jobs/jobstest"
)

// clock is a settable time source.
type clock struct {
	now   time.Time
	mutex sync.Mutex
}

func (fake *clock) Now() time.Time {
	fake.mutex.Lock()
	defer fake.mutex.Unlock()
	return fake.now
}

func (fake *clock) advance(duration time.Duration) {
	fake.mutex.Lock()
	defer fake.mutex.Unlock()
	fake.now = fake.now.Add(duration)
}

func withClock(fake *clock) func(*machinetranslation.Settings) {
	return func(settings *machinetranslation.Settings) { settings.Now = fake.Now }
}

func newClock() *clock {
	return &clock{now: time.Date(2026, 9, 25, 12, 0, 0, 0, time.UTC)}
}

func TestRateLimitedJobsLeaseTheirTextsUntilTheyRunAgain(t *testing.T) {
	t.Parallel()
	now := newClock()
	milanesa := text("Milanesa")
	texts := newFakeTexts(milanesa)
	tested := newHarness(t, texts, withClock(now))
	tested.translator.failure = machinetranslation.RateLimitedError{RetryAfter: 30 * time.Second}
	err := jobstest.Run(t, tested.machine.TranslateJob(), arguments(english, "", milanesa), jobstest.Tenant("acme"))
	if duration, ok := jobs.SnoozeDuration(err); !ok || duration != 30*time.Second {
		t.Fatalf("Run = %v, want a 30s snooze", err)
	}
	if until := texts.leased[milanesa.ID]; until.Before(now.Now().Add(30 * time.Second)) {
		t.Fatalf("leased until %s, want at least the snooze", until)
	}
}

func TestTransientFailuresLeaseTheirTextsPastTheNextRetry(t *testing.T) {
	t.Parallel()
	now := newClock()
	milanesa := text("Milanesa")
	texts := newFakeTexts(milanesa)
	tested := newHarness(t, texts, withClock(now))
	tested.translator.failure = errors.New("connection reset")
	err := jobstest.Run(t, tested.machine.TranslateJob(), arguments(english, "", milanesa), jobstest.Tenant("acme"), jobstest.Attempt(4), jobstest.MaxAttempts(5))
	if err == nil || jobs.IsCancel(err) {
		t.Fatalf("Run = %v, want a retry", err)
	}
	// The fourth failure retries after about 4^4 = 256 seconds.
	if until := texts.leased[milanesa.ID]; until.Before(now.Now().Add(256 * time.Second)) {
		t.Fatalf("leased until %s, want past the next retry", until)
	}
}

func TestQuotaExhaustionPausesEveryJobUntilTheQuotaPauseEnds(t *testing.T) {
	t.Parallel()
	now := newClock()
	milanesa, empanada := text("Milanesa"), text("Empanada")
	texts := newFakeTexts(milanesa, empanada)
	tested := newHarness(t, texts, withClock(now), func(settings *machinetranslation.Settings) { settings.QuotaPause = time.Hour })
	tested.translator.failure = machinetranslation.ErrQuotaExceeded
	if err := jobstest.Run(t, tested.machine.TranslateJob(), arguments(english, "", milanesa), jobstest.Tenant("acme")); !isSnooze(err, time.Hour) {
		t.Fatalf("first Run = %v, want a one hour snooze", err)
	}
	tested.translator.failure = nil
	now.advance(10 * time.Minute)
	if err := jobstest.Run(t, tested.machine.TranslateJob(), arguments(english, "", empanada), jobstest.Tenant("acme")); !isSnooze(err, 50*time.Minute) {
		t.Fatalf("Run while paused = %v, want a snooze until the pause ends", err)
	}
	if len(tested.translator.requests) != 1 {
		t.Fatalf("DeepL calls = %d, want none while paused", len(tested.translator.requests))
	}
	if until := texts.leased[empanada.ID]; until.Before(now.Now().Add(50 * time.Minute)) {
		t.Fatalf("leased until %s, want past the pause", until)
	}
	now.advance(time.Hour)
	if err := jobstest.Run(t, tested.machine.TranslateJob(), arguments(english, "", empanada), jobstest.Tenant("acme")); err != nil {
		t.Fatalf("Run after the pause = %v", err)
	}
	if texts.stored[empanada.ID] == "" {
		t.Fatal("nothing stored after the pause")
	}
}

func isSnooze(err error, want time.Duration) bool {
	duration, ok := jobs.SnoozeDuration(err)
	return ok && duration == want
}

func TestPermanentRejectionsMarkTheTextsFailed(t *testing.T) {
	t.Parallel()
	milanesa := text("Milanesa")
	texts := newFakeTexts(milanesa)
	tested := newHarness(t, texts)
	tested.translator.failure = machinetranslation.PermanentError{Cause: errors.New("unsupported")}
	if err := jobstest.Run(t, tested.machine.TranslateJob(), arguments(english, "", milanesa), jobstest.Tenant("acme")); !jobs.IsCancel(err) {
		t.Fatalf("Run = %v, want a cancel", err)
	}
	if !texts.failed[milanesa.ID] {
		t.Fatal("the rejected text was not marked failed")
	}
}

func TestBatchesAreSplitByEncodedSize(t *testing.T) {
	t.Parallel()
	small := []localizedtext.Text{text(strings.Repeat("a", 40)), text(strings.Repeat("b", 40)), text(strings.Repeat("c", 40))}
	huge := text(strings.Repeat("d", 400))
	texts := newFakeTexts(append(slices.Clone(small), huge)...)
	tested := newHarness(t, texts, func(settings *machinetranslation.Settings) {
		settings.MaxRequestBytes = 350
		settings.BatchSize = 10
	})
	if err := jobstest.Run(t, tested.machine.TranslateJob(), arguments(english, "", append(slices.Clone(small), huge)...), jobstest.Tenant("acme")); err != nil {
		t.Fatalf("Run: %v", err)
	}
	for _, request := range tested.translator.requests {
		if len(request.Texts) > 1 && len(strings.Join(request.Texts, "")) > 100 {
			t.Errorf("request of %d texts exceeds the size budget", len(request.Texts))
		}
	}
	if len(tested.translator.requests) < 2 {
		t.Fatalf("requests = %d, want the batch split by size", len(tested.translator.requests))
	}
	for _, sent := range small {
		if texts.stored[sent.ID] == "" {
			t.Errorf("%s was not stored", sent.SourceValue[:1])
		}
	}
	if !texts.failed[huge.ID] || texts.stored[huge.ID] != "" {
		t.Fatal("a text over the size budget alone must be marked failed without being sent")
	}
}

func TestPayloadTooLargeSplitsTheBatch(t *testing.T) {
	t.Parallel()
	first, second, lonely := text("Milanesa"), text("Empanada"), text("Locro")
	texts := newFakeTexts(first, second, lonely)
	tested := newHarness(t, texts, func(settings *machinetranslation.Settings) { settings.BatchSize = 10 })
	tested.translator.fail = func(request machinetranslation.Request) error {
		if len(request.Texts) > 1 || request.Texts[0] == "Locro" {
			return machinetranslation.ErrPayloadTooLarge
		}
		return nil
	}
	if err := jobstest.Run(t, tested.machine.TranslateJob(), arguments(english, "", first, second, lonely), jobstest.Tenant("acme")); err != nil {
		t.Fatalf("Run: %v", err)
	}
	if texts.stored[first.ID] == "" || texts.stored[second.ID] == "" {
		t.Fatalf("stored = %v, want both texts after splitting", texts.stored)
	}
	if !texts.failed[lonely.ID] {
		t.Fatal("a single text still too large must be marked failed")
	}
}

func TestSweepsVisitEachTenantOnceInOrder(t *testing.T) {
	t.Parallel()
	texts := newFakeTexts()
	texts.tenants = []string{"globex", "acme", "globex"}
	tested := newHarness(t, texts)
	if err := jobstest.Run(t, tested.machine.RequestExpiredJob(), machinetranslation.SweepArguments{}); err != nil {
		t.Fatalf("expired sweep: %v", err)
	}
	if got := strings.Join(texts.tenantsSeen, "|"); got != "|acme|globex" {
		t.Fatalf("transaction tenants = %q, want |acme|globex", got)
	}
}
