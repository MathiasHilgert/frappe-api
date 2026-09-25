// Package machinetranslation machine translates localized texts in the
// background and runs their periodic sweeps.
//
// The Translator port lives here, next to the jobs that consume it,
// rather than in i18n (the locale model and static catalogs) or
// localizedtext (storage and the read API): neither of them calls a
// provider, and keeping them provider-free keeps the module-facing API
// small. The deepl subpackage is the production adapter.
//
// # Flow
//
// Requester is the localizedtext.TranslationRequester. Inside the
// caller's transaction it enqueues one localized_texts.machine_translate
// job per locale, context and batch of text IDs with the source hashes
// they were requested for; the jobs Tenancy captures the transaction's
// tenant. The handler then:
//
//  1. loads the texts in a transaction for the job's tenant, skips those
//     deleted, changed since the request, translated manually or already
//     current, and leases the rest (their pending requested_at moves past
//     the attempt), so reads and the expired sweep never request a text a
//     live job works on;
//  2. calls the Translator outside any transaction, one request per
//     source locale and context (the text's own, else the one requested,
//     the Field default), split by encoded size (MaxRequestBytes);
//  3. stores each result with SetMachineTranslation in a new tenant
//     transaction, which discards it if the source changed meanwhile or
//     a manual translation appeared.
//
// A RateLimitedError snoozes the job. ErrQuotaExceeded and
// ErrUnauthorized open a process-wide circuit: every job snoozes until
// QuotaPause ends, logged once per pause. ErrPayloadTooLarge splits the
// batch. A PermanentError, or a single text too large, marks the texts
// failed (localizedtext.StatusFailed: not requested again until the
// source changes). Any other error retries with backoff. Every snooze and
// retry extends the lease past the next attempt.
//
// Deduplication is the pending row, not River unique jobs: River requires
// running jobs in every uniqueness state set, so a unique job would drop
// a new request (source changed again, translation deleted) while an
// equal job is still running after it loaded its texts.
//
// # Tenancy
//
// There is no tenant model yet. The tenant is the transaction-local
// application.tenant setting Row Level Security uses: captured at enqueue
// and restored by the Transactor. Global texts (no tenant) are never
// requested (localizedtext only marks the tenant's own texts pending),
// and a job without a tenant is cancelled. The sweeps list tenants with
// localizedtext.Service.Tenants and run once per tenant transaction.
package machinetranslation
