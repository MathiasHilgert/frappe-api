// Package postgres is the Postgres localizedtext.Store, backed by the
// locales, localized_texts and localized_text_translations tables created
// by migrations/20260925230000_localized_texts.sql.
//
// Every method joins the caller's transaction, found in ctx with
// database.TransactionFromContext, and fails with ErrNoTransaction outside
// one: texts are written atomically with the entity that references them,
// and Row Level Security scopes every statement to the transaction's
// application.tenant setting (a new text takes its tenant_id from it).
package postgres

import (
	"context"
	"errors"
	"fmt"
	"strings"
	"time"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/database"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n/localizedtext"
)

// ErrNoTransaction is returned by every method when ctx carries no
// database transaction (database.WithinTransaction).
var ErrNoTransaction = errors.New("postgres localizedtext: must run inside a database transaction (database.WithinTransaction)")

// Store is the Postgres localizedtext.Store.
type Store struct{}

// NewStore returns a Store. It holds no pool: it always runs in the
// caller's transaction.
func NewStore() *Store {
	return &Store{}
}

var _ localizedtext.Store = (*Store)(nil)

func transaction(ctx context.Context) (pgx.Tx, error) {
	current, ok := database.TransactionFromContext(ctx)
	if !ok {
		return nil, ErrNoTransaction
	}
	return current, nil
}

// Create implements localizedtext.Store.
func (*Store) Create(ctx context.Context, text localizedtext.Text) error {
	current, err := transaction(ctx)
	if err != nil {
		return err
	}
	_, err = current.Exec(ctx, `INSERT INTO localized_texts (id, source_locale, source_value, source_hash, context)
		VALUES ($1, $2, $3, $4, nullif($5, ''))`,
		uuid.UUID(text.ID), text.SourceLocale.String(), text.SourceValue, text.SourceHash, text.Context)
	if err != nil {
		return fmt.Errorf("create localized text: %w", err)
	}
	return nil
}

// UpdateSource implements localizedtext.Store.
func (store *Store) UpdateSource(ctx context.Context, id localizedtext.ID, source localizedtext.Source, hash string) (localizedtext.Text, error) {
	current, err := transaction(ctx)
	if err != nil {
		return localizedtext.Text{}, err
	}
	var previousHash string
	// FOR UPDATE on a row the tenant cannot update (another tenant's, or
	// a global text) returns nothing; writable tells the two apart.
	err = current.QueryRow(ctx, `SELECT source_hash FROM localized_texts
		WHERE id = $1 AND tenant_id = current_setting('application.tenant', true) FOR UPDATE`,
		uuid.UUID(id)).Scan(&previousHash)
	if errors.Is(err, pgx.ErrNoRows) {
		return localizedtext.Text{}, notWritable(ctx, current, id)
	}
	if err != nil {
		return localizedtext.Text{}, fmt.Errorf("lock localized text: %w", err)
	}
	// Named arguments: each statement uses only some of them. An empty
	// context keeps the stored one unless clear is set.
	arguments := pgx.NamedArgs{
		"id": uuid.UUID(id), "locale": source.Locale.String(), "value": source.Value,
		"hash": hash, "context": source.Context, "clear": source.ClearContext,
	}
	statements := []string{
		`UPDATE localized_texts SET source_locale = @locale, source_value = @value, source_hash = @hash,
			context = CASE WHEN @clear THEN NULL ELSE coalesce(nullif(@context, ''), context) END,
			updated_at = now() WHERE id = @id`,
	}
	if previousHash != hash {
		statements = append(statements,
			// A translation into the new source locale is now the source.
			`DELETE FROM localized_text_translations WHERE text_id = @id AND locale = @locale`,
			// A requested translation without a value would arrive for the
			// old source and be discarded: drop it so it is requested again.
			`DELETE FROM localized_text_translations
				WHERE text_id = @id AND status = 'pending' AND value IS NULL AND source_hash <> @hash`,
			`UPDATE localized_text_translations SET status = 'stale'
				WHERE text_id = @id AND source_hash <> @hash AND status <> 'stale' AND value IS NOT NULL`,
		)
	}
	for _, statement := range statements {
		if _, err := current.Exec(ctx, statement, arguments); err != nil {
			return localizedtext.Text{}, fmt.Errorf("update localized text source: %w", err)
		}
	}
	return store.Get(ctx, id)
}

// notWritable returns ErrReadOnlyText when the tenant can see text id
// (a global text) and ErrNotFound otherwise.
func notWritable(ctx context.Context, current pgx.Tx, id localizedtext.ID) error {
	var visible bool
	if err := current.QueryRow(ctx, `SELECT EXISTS (SELECT 1 FROM localized_texts WHERE id = $1)`, uuid.UUID(id)).Scan(&visible); err != nil {
		return fmt.Errorf("check localized text: %w", err)
	}
	if visible {
		return localizedtext.ErrReadOnlyText
	}
	return localizedtext.ErrNotFound
}

// SetManualTranslation implements localizedtext.Store. Like
// SetMachineTranslation, it share-locks the text, so a concurrent
// UpdateSource either commits first (and its new hash is used) or waits
// until this translation is stored (and then marks it stale).
func (*Store) SetManualTranslation(ctx context.Context, id localizedtext.ID, locale i18n.Locale, value string) error {
	current, err := transaction(ctx)
	if err != nil {
		return err
	}
	tag, err := current.Exec(ctx, `INSERT INTO localized_text_translations
			(text_id, locale, value, origin, status, source_hash, translated_at)
		SELECT id, $2, $3, 'manual', 'current', source_hash, now() FROM localized_texts
		WHERE id = $1 AND tenant_id = current_setting('application.tenant', true) FOR SHARE
		ON CONFLICT (text_id, locale) DO UPDATE SET value = excluded.value, origin = 'manual',
			status = 'current', source_hash = excluded.source_hash, translated_at = now()`,
		uuid.UUID(id), locale.String(), value)
	if err != nil {
		return fmt.Errorf("set manual translation: %w", err)
	}
	if tag.RowsAffected() == 0 {
		return notWritable(ctx, current, id)
	}
	return nil
}

// SetMachineTranslation implements localizedtext.Store. The text row is
// share-locked, so a concurrent UpdateSource either commits first (and
// the hash no longer matches) or waits until this translation is stored
// (and then marks it stale).
func (*Store) SetMachineTranslation(ctx context.Context, id localizedtext.ID, locale i18n.Locale, value, sourceHash string) (bool, error) {
	current, err := transaction(ctx)
	if err != nil {
		return false, err
	}
	tag, err := current.Exec(ctx, `INSERT INTO localized_text_translations
			(text_id, locale, value, origin, status, source_hash, translated_at)
		SELECT id, $2, $3, 'machine', 'current', source_hash, now() FROM localized_texts
		WHERE id = $1 AND source_hash = $4 FOR SHARE
		ON CONFLICT (text_id, locale) DO UPDATE SET value = excluded.value, status = 'current',
			source_hash = excluded.source_hash, translated_at = now()
		WHERE localized_text_translations.origin = 'machine'`,
		uuid.UUID(id), locale.String(), value, sourceHash)
	if err != nil {
		return false, fmt.Errorf("set machine translation: %w", err)
	}
	return tag.RowsAffected() == 1, nil
}

// MarkPending implements localizedtext.Store.
func (*Store) MarkPending(ctx context.Context, requests []localizedtext.TranslationRequest, pendingTimeout time.Duration) ([]localizedtext.TranslationRequest, error) {
	current, err := transaction(ctx)
	if err != nil {
		return nil, err
	}
	ids := make([]uuid.UUID, len(requests))
	locales := make([]string, len(requests))
	for index, request := range requests {
		ids[index] = uuid.UUID(request.TextID)
		locales[index] = request.Locale.String()
	}
	// Only the tenant's own texts are marked: a global text is readable
	// but not writable, and is translated by its maintainers. The row
	// takes the text's current hash, whatever the request carried.
	rows, err := current.Query(ctx, `INSERT INTO localized_text_translations
			(text_id, locale, value, origin, status, source_hash, requested_at, attempts)
		SELECT requested.text_id, requested.locale, NULL, 'machine', 'pending', localized_texts.source_hash, now(), 1
		FROM unnest($1::uuid[], $2::text[]) AS requested (text_id, locale)
		JOIN localized_texts ON localized_texts.id = requested.text_id
			AND localized_texts.tenant_id = current_setting('application.tenant', true)
		ON CONFLICT (text_id, locale) DO UPDATE SET status = 'pending', source_hash = excluded.source_hash,
			requested_at = now(), attempts = localized_text_translations.attempts + 1
		WHERE localized_text_translations.origin = 'machine' AND (
			localized_text_translations.status = 'stale'
			OR (localized_text_translations.status = 'pending'
				AND localized_text_translations.requested_at < now() - make_interval(secs => $3)))
		RETURNING text_id, locale`, ids, locales, pendingTimeout.Seconds())
	if err != nil {
		return nil, fmt.Errorf("mark translations pending: %w", err)
	}
	marked := map[string]bool{}
	for rows.Next() {
		var id uuid.UUID
		var locale string
		if err := rows.Scan(&id, &locale); err != nil {
			return nil, fmt.Errorf("scan pending translation: %w", err)
		}
		marked[id.String()+" "+locale] = true
	}
	if err := rows.Err(); err != nil {
		return nil, fmt.Errorf("mark translations pending: %w", err)
	}
	recorded := make([]localizedtext.TranslationRequest, 0, len(marked))
	for _, request := range requests {
		if marked[request.TextID.String()+" "+request.Locale.String()] {
			recorded = append(recorded, request)
		}
	}
	return recorded, nil
}

// ExpiredPending implements localizedtext.Store.
func (*Store) ExpiredPending(ctx context.Context, olderThan time.Duration, limit int) ([]localizedtext.TranslationRequest, error) {
	current, err := transaction(ctx)
	if err != nil {
		return nil, err
	}
	rows, err := current.Query(ctx, `SELECT translations.text_id, translations.locale, localized_texts.source_hash,
			coalesce(localized_texts.context, '')
		FROM localized_text_translations AS translations
		JOIN localized_texts ON localized_texts.id = translations.text_id
		WHERE localized_texts.tenant_id = current_setting('application.tenant', true)
			AND translations.status = 'pending'
			AND translations.requested_at < now() - make_interval(secs => $1)
		ORDER BY translations.requested_at
		LIMIT $2`, olderThan.Seconds(), limit)
	if err != nil {
		return nil, fmt.Errorf("load expired pending translations: %w", err)
	}
	defer rows.Close()
	var requests []localizedtext.TranslationRequest
	for rows.Next() {
		var id uuid.UUID
		var locale string
		request := localizedtext.TranslationRequest{Reason: localizedtext.ReasonExpired}
		if scanError := rows.Scan(&id, &locale, &request.SourceHash, &request.Context); scanError != nil {
			return nil, fmt.Errorf("scan expired pending translation: %w", scanError)
		}
		request.TextID = localizedtext.ID(id)
		if request.Locale, err = i18n.ParseLocale(locale); err != nil {
			return nil, fmt.Errorf("stored translation locale: %w", err)
		}
		requests = append(requests, request)
	}
	if err := rows.Err(); err != nil {
		return nil, fmt.Errorf("load expired pending translations: %w", err)
	}
	return requests, nil
}

// References implements localizedtext.Store from pg_constraint:
// single-column foreign keys to localized_texts (id) in any table but the
// localized text tables themselves.
func (*Store) References(ctx context.Context) ([]localizedtext.Reference, error) {
	current, err := transaction(ctx)
	if err != nil {
		return nil, err
	}
	rows, err := current.Query(ctx, `SELECT class.relname, attribute.attname,
			CASE constraints.confdeltype
				WHEN 'a' THEN 'no action' WHEN 'r' THEN 'restrict' WHEN 'c' THEN 'cascade'
				WHEN 'n' THEN 'set null' ELSE 'set default' END
		FROM pg_constraint AS constraints
		JOIN pg_class AS class ON class.oid = constraints.conrelid
		JOIN pg_attribute AS attribute ON attribute.attrelid = constraints.conrelid
			AND attribute.attnum = constraints.conkey[1]
		WHERE constraints.contype = 'f'
			AND constraints.confrelid = 'localized_texts'::regclass
			AND constraints.conrelid <> 'localized_text_translations'::regclass
		ORDER BY class.relname, attribute.attname`)
	if err != nil {
		return nil, fmt.Errorf("load localized text references: %w", err)
	}
	defer rows.Close()
	var references []localizedtext.Reference
	for rows.Next() {
		var reference localizedtext.Reference
		var onDelete string
		if err := rows.Scan(&reference.Table, &reference.Column, &onDelete); err != nil {
			return nil, fmt.Errorf("scan localized text reference: %w", err)
		}
		reference.OnDelete = localizedtext.OnDelete(onDelete)
		references = append(references, reference)
	}
	if err := rows.Err(); err != nil {
		return nil, fmt.Errorf("load localized text references: %w", err)
	}
	return references, nil
}

// Referencing implements localizedtext.Store with one query: a UNION ALL
// over references, in order, keeping the first match per text.
// Identifiers are quoted with Sanitize.
func (*Store) Referencing(ctx context.Context, references []localizedtext.Reference, ids []localizedtext.ID) (map[localizedtext.ID]localizedtext.Reference, error) {
	current, err := transaction(ctx)
	if err != nil {
		return nil, err
	}
	found := map[localizedtext.ID]localizedtext.Reference{}
	if len(references) == 0 || len(ids) == 0 {
		return found, nil
	}
	selects := make([]string, len(references))
	for index, reference := range references {
		selects[index] = fmt.Sprintf("SELECT %d AS position, %s AS text_id FROM %s WHERE %s = ANY($1::uuid[])",
			index, pgx.Identifier{reference.Column}.Sanitize(), pgx.Identifier{reference.Table}.Sanitize(),
			pgx.Identifier{reference.Column}.Sanitize())
	}
	rows, err := current.Query(ctx, `SELECT DISTINCT ON (text_id) text_id, position FROM (`+
		strings.Join(selects, " UNION ALL ")+`) AS referencing ORDER BY text_id, position`, toUUIDs(ids))
	if err != nil {
		return nil, fmt.Errorf("load referencing fields: %w", err)
	}
	defer rows.Close()
	for rows.Next() {
		var id uuid.UUID
		var position int
		if err := rows.Scan(&id, &position); err != nil {
			return nil, fmt.Errorf("scan referencing field: %w", err)
		}
		found[localizedtext.ID(id)] = references[position]
	}
	if err := rows.Err(); err != nil {
		return nil, fmt.Errorf("load referencing fields: %w", err)
	}
	return found, nil
}

// Tenants implements localizedtext.Store through the SECURITY DEFINER
// function localized_text_tenants() (migration
// 20260927000000_localized_text_tenants.sql), which crosses Row Level
// Security and returns tenant identifiers only.
func (*Store) Tenants(ctx context.Context) ([]string, error) {
	current, err := transaction(ctx)
	if err != nil {
		return nil, err
	}
	rows, err := current.Query(ctx, `SELECT tenant FROM localized_text_tenants() AS tenant`)
	if err != nil {
		return nil, fmt.Errorf("list localized text tenants: %w", err)
	}
	tenants, err := pgx.CollectRows(rows, pgx.RowTo[string])
	if err != nil {
		return nil, fmt.Errorf("list localized text tenants: %w", err)
	}
	return tenants, nil
}

// Isolate implements localizedtext.Store with a savepoint (a nested
// database.WithinTransaction), so a failed work leaves the caller's
// transaction usable.
func (*Store) Isolate(ctx context.Context, work func(ctx context.Context) error) error {
	if _, err := transaction(ctx); err != nil {
		return err
	}
	return database.WithinTransaction(ctx, nil, nil, func(ctx context.Context, _ pgx.Tx) error {
		return work(ctx)
	})
}

// selectTexts selects texts with their translations (left joined);
// callers append the join condition's locale filter and WHERE clause.
const selectTexts = `SELECT localized_texts.id, localized_texts.source_locale, localized_texts.source_value,
		localized_texts.source_hash, coalesce(localized_texts.context, ''), localized_texts.created_at,
		localized_texts.updated_at, translations.locale, translations.value, translations.origin,
		translations.status, translations.source_hash, translations.translated_at,
		translations.requested_at, coalesce(translations.attempts, 0)
	FROM localized_texts
	LEFT JOIN localized_text_translations AS translations ON translations.text_id = localized_texts.id`

// Get implements localizedtext.Store.
func (*Store) Get(ctx context.Context, id localizedtext.ID) (localizedtext.Text, error) {
	texts, err := query(ctx, selectTexts+` WHERE localized_texts.id = $1 ORDER BY translations.locale`, uuid.UUID(id))
	if err != nil {
		return localizedtext.Text{}, err
	}
	if len(texts) == 0 {
		return localizedtext.Text{}, localizedtext.ErrNotFound
	}
	return texts[0], nil
}

// LoadForLocale implements localizedtext.Store.
func (*Store) LoadForLocale(ctx context.Context, ids []localizedtext.ID, locale i18n.Locale) ([]localizedtext.Text, error) {
	if len(ids) == 0 {
		return nil, nil
	}
	return query(ctx, selectTexts+` AND translations.locale = $2 WHERE localized_texts.id = ANY($1::uuid[])`,
		toUUIDs(ids), locale.String())
}

// Delete implements localizedtext.Store.
func (*Store) Delete(ctx context.Context, ids []localizedtext.ID) (int64, error) {
	current, err := transaction(ctx)
	if err != nil {
		return 0, err
	}
	tag, err := current.Exec(ctx, `DELETE FROM localized_texts WHERE id = ANY($1::uuid[])`, toUUIDs(ids))
	if err != nil {
		return 0, fmt.Errorf("delete localized texts: %w", err)
	}
	return tag.RowsAffected(), nil
}

// DeleteOrphans implements localizedtext.Store in two statements. The
// first locks candidates with SKIP LOCKED, so a text a concurrent
// transaction is starting to reference (its foreign key check
// key-share-locks it) is skipped. The second deletes them after checking
// again, on a fresh READ COMMITTED snapshot, that no reference committed
// in between: once locked, no new reference can appear, so a foreign key
// violation can never abort the batch.
func (*Store) DeleteOrphans(ctx context.Context, references []localizedtext.Reference, olderThan time.Duration, limit int) (int64, error) {
	current, err := transaction(ctx)
	if err != nil {
		return 0, err
	}
	unreferenced := unreferencedCondition(references)
	var candidates []uuid.UUID
	rows, err := current.Query(ctx, `SELECT candidate.id FROM localized_texts AS candidate
		WHERE candidate.tenant_id = current_setting('application.tenant', true)
			AND candidate.updated_at < now() - make_interval(secs => $1)`+unreferenced+`
		ORDER BY candidate.updated_at
		LIMIT $2
		FOR UPDATE SKIP LOCKED`, olderThan.Seconds(), limit)
	if err != nil {
		return 0, fmt.Errorf("lock orphan localized texts: %w", err)
	}
	candidates, err = pgx.CollectRows(rows, pgx.RowTo[uuid.UUID])
	if err != nil {
		return 0, fmt.Errorf("lock orphan localized texts: %w", err)
	}
	if len(candidates) == 0 {
		return 0, nil
	}
	tag, err := current.Exec(ctx, `DELETE FROM localized_texts AS candidate
		WHERE candidate.id = ANY($1::uuid[])`+unreferenced, candidates)
	if err != nil {
		return 0, fmt.Errorf("delete orphan localized texts: %w", err)
	}
	return tag.RowsAffected(), nil
}

// unreferencedCondition returns "AND NOT EXISTS" clauses, one per
// reference, over the alias candidate. Identifiers come from the catalog
// and are quoted with Sanitize.
func unreferencedCondition(references []localizedtext.Reference) string {
	var condition strings.Builder
	for _, reference := range references {
		fmt.Fprintf(&condition, "\n\t\t\tAND NOT EXISTS (SELECT 1 FROM %s WHERE %s = candidate.id)",
			pgx.Identifier{reference.Table}.Sanitize(), pgx.Identifier{reference.Column}.Sanitize())
	}
	return condition.String()
}

func toUUIDs(ids []localizedtext.ID) []uuid.UUID {
	converted := make([]uuid.UUID, len(ids))
	for index, id := range ids {
		converted[index] = uuid.UUID(id)
	}
	return converted
}

// row is one text joined with at most one translation.
type row struct {
	createdAt             time.Time
	updatedAt             time.Time
	translationSourceHash *string
	locale                *string
	value                 *string
	status                *string
	origin                *string
	translatedAt          *time.Time
	requestedAt           *time.Time
	sourceHash            string
	context               string
	sourceValue           string
	sourceLocale          string
	attempts              int
	id                    uuid.UUID
}

// query runs statement and folds its rows into texts, keeping order.
func query(ctx context.Context, statement string, arguments ...any) ([]localizedtext.Text, error) {
	current, err := transaction(ctx)
	if err != nil {
		return nil, err
	}
	rows, err := current.Query(ctx, statement, arguments...)
	if err != nil {
		return nil, fmt.Errorf("load localized texts: %w", err)
	}
	defer rows.Close()
	var texts []localizedtext.Text
	positions := map[uuid.UUID]int{}
	for rows.Next() {
		var scanned row
		if err := rows.Scan(&scanned.id, &scanned.sourceLocale, &scanned.sourceValue, &scanned.sourceHash,
			&scanned.context, &scanned.createdAt, &scanned.updatedAt, &scanned.locale, &scanned.value,
			&scanned.origin, &scanned.status, &scanned.translationSourceHash, &scanned.translatedAt,
			&scanned.requestedAt, &scanned.attempts); err != nil {
			return nil, fmt.Errorf("scan localized text: %w", err)
		}
		position, seen := positions[scanned.id]
		if !seen {
			text, err := scanned.text()
			if err != nil {
				return nil, err
			}
			position = len(texts)
			positions[scanned.id] = position
			texts = append(texts, text)
		}
		if scanned.locale == nil {
			continue
		}
		translation, err := scanned.translation()
		if err != nil {
			return nil, err
		}
		texts[position].Translations = append(texts[position].Translations, translation)
	}
	if err := rows.Err(); err != nil {
		return nil, fmt.Errorf("load localized texts: %w", err)
	}
	return texts, nil
}

func (scanned row) text() (localizedtext.Text, error) {
	locale, err := i18n.ParseLocale(scanned.sourceLocale)
	if err != nil {
		return localizedtext.Text{}, fmt.Errorf("stored source locale: %w", err)
	}
	return localizedtext.Text{
		ID:           localizedtext.ID(scanned.id),
		SourceLocale: locale,
		SourceValue:  scanned.sourceValue,
		SourceHash:   scanned.sourceHash,
		Context:      scanned.context,
		CreatedAt:    scanned.createdAt,
		UpdatedAt:    scanned.updatedAt,
	}, nil
}

func (scanned row) translation() (localizedtext.Translation, error) {
	locale, err := i18n.ParseLocale(*scanned.locale)
	if err != nil {
		return localizedtext.Translation{}, fmt.Errorf("stored translation locale: %w", err)
	}
	translation := localizedtext.Translation{
		Locale:       locale,
		Origin:       localizedtext.Origin(*scanned.origin),
		Status:       localizedtext.Status(*scanned.status),
		SourceHash:   *scanned.translationSourceHash,
		TranslatedAt: *scanned.translatedAt,
		Attempts:     scanned.attempts,
	}
	if scanned.requestedAt != nil {
		translation.RequestedAt = *scanned.requestedAt
	}
	if scanned.value != nil {
		translation.Value = *scanned.value
	}
	return translation, nil
}

// ErrMissingLocales reports configured locales absent from the locales
// table: texts in them could not be stored.
var ErrMissingLocales = errors.New("postgres localizedtext: configured locales are missing from the locales table (add a migration)")

// Querier is what CheckLocales queries: a pool or a transaction.
type Querier interface {
	Query(ctx context.Context, sql string, arguments ...any) (pgx.Rows, error)
}

// CheckLocales fails with ErrMissingLocales when any of locales is not in
// the locales table. The composition root runs it at startup, so a
// supported locale without a migration stops the application instead of
// failing every write in it.
func CheckLocales(ctx context.Context, querier Querier, locales []i18n.Locale) error {
	codes := make([]string, len(locales))
	for index, locale := range locales {
		codes[index] = locale.String()
	}
	rows, err := querier.Query(ctx, `SELECT configured.code FROM unnest($1::text[]) AS configured (code)
		WHERE NOT EXISTS (SELECT 1 FROM locales WHERE locales.code = configured.code)
		ORDER BY configured.code`, codes)
	if err != nil {
		return fmt.Errorf("check locales: %w", err)
	}
	missing, err := pgx.CollectRows(rows, pgx.RowTo[string])
	if err != nil {
		return fmt.Errorf("check locales: %w", err)
	}
	if len(missing) > 0 {
		return fmt.Errorf("%w: %s", ErrMissingLocales, strings.Join(missing, ", "))
	}
	return nil
}
