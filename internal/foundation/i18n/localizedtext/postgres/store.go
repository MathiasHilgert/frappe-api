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
func (store *Store) UpdateSource(ctx context.Context, text localizedtext.Text) (localizedtext.Text, error) {
	current, err := transaction(ctx)
	if err != nil {
		return localizedtext.Text{}, err
	}
	var previousHash string
	// FOR UPDATE on a row the tenant cannot update (another tenant's, or
	// a global text) returns nothing, like an unknown ID.
	err = current.QueryRow(ctx, `SELECT source_hash FROM localized_texts
		WHERE id = $1 AND tenant_id = current_setting('application.tenant', true) FOR UPDATE`,
		uuid.UUID(text.ID)).Scan(&previousHash)
	if errors.Is(err, pgx.ErrNoRows) {
		return localizedtext.Text{}, localizedtext.ErrNotFound
	}
	if err != nil {
		return localizedtext.Text{}, fmt.Errorf("lock localized text: %w", err)
	}
	// Named arguments: each statement uses only some of them.
	arguments := pgx.NamedArgs{
		"id": uuid.UUID(text.ID), "locale": text.SourceLocale.String(), "value": text.SourceValue,
		"hash": text.SourceHash, "context": text.Context,
	}
	statements := []string{
		`UPDATE localized_texts SET source_locale = @locale, source_value = @value, source_hash = @hash,
			context = nullif(@context, ''), updated_at = now() WHERE id = @id`,
	}
	if previousHash != text.SourceHash {
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
	return store.Get(ctx, text.ID)
}

// SetManualTranslation implements localizedtext.Store.
func (*Store) SetManualTranslation(ctx context.Context, id localizedtext.ID, locale i18n.Locale, value string) error {
	current, err := transaction(ctx)
	if err != nil {
		return err
	}
	tag, err := current.Exec(ctx, `INSERT INTO localized_text_translations
			(text_id, locale, value, origin, status, source_hash, translated_at)
		SELECT id, $2, $3, 'manual', 'current', source_hash, now() FROM localized_texts WHERE id = $1
		ON CONFLICT (text_id, locale) DO UPDATE SET value = excluded.value, origin = 'manual',
			status = 'current', source_hash = excluded.source_hash, translated_at = now()`,
		uuid.UUID(id), locale.String(), value)
	if err != nil {
		return fmt.Errorf("set manual translation: %w", err)
	}
	if tag.RowsAffected() == 0 {
		return localizedtext.ErrNotFound
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
func (*Store) MarkPending(ctx context.Context, requests []localizedtext.TranslationRequest) ([]localizedtext.TranslationRequest, error) {
	current, err := transaction(ctx)
	if err != nil {
		return nil, err
	}
	ids := make([]uuid.UUID, len(requests))
	locales := make([]string, len(requests))
	hashes := make([]string, len(requests))
	for index, request := range requests {
		ids[index] = uuid.UUID(request.TextID)
		locales[index] = request.Locale.String()
		hashes[index] = request.SourceHash
	}
	// Only the tenant's own texts are marked: a global text is readable
	// but not writable, and is translated by its maintainers.
	rows, err := current.Query(ctx, `INSERT INTO localized_text_translations (text_id, locale, value, origin, status, source_hash)
		SELECT requested.text_id, requested.locale, NULL, 'machine', 'pending', requested.source_hash
		FROM unnest($1::uuid[], $2::text[], $3::text[]) AS requested (text_id, locale, source_hash)
		JOIN localized_texts ON localized_texts.id = requested.text_id
			AND localized_texts.tenant_id = current_setting('application.tenant', true)
		ON CONFLICT (text_id, locale) DO UPDATE SET status = 'pending'
		WHERE localized_text_translations.origin = 'machine' AND localized_text_translations.status = 'stale'
		RETURNING text_id, locale`, ids, locales, hashes)
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

// selectTexts selects texts with their translations (left joined);
// callers append the join condition's locale filter and WHERE clause.
const selectTexts = `SELECT localized_texts.id, localized_texts.source_locale, localized_texts.source_value,
		localized_texts.source_hash, coalesce(localized_texts.context, ''), localized_texts.created_at,
		localized_texts.updated_at, translations.locale, translations.value, translations.origin,
		translations.status, translations.source_hash, translations.translated_at
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

// DeleteOrphans implements localizedtext.Store. Candidates are locked
// with SKIP LOCKED: a text a concurrent transaction is starting to
// reference (its foreign key check key-share-locks the text) is skipped,
// never deleted under it.
func (*Store) DeleteOrphans(ctx context.Context, fields []localizedtext.Field, olderThan time.Duration, limit int) (int64, error) {
	current, err := transaction(ctx)
	if err != nil {
		return 0, err
	}
	var references strings.Builder
	for _, field := range fields {
		// Field.validate (localizedtext.Service.Field) guarantees plain
		// identifiers; Sanitize quotes them regardless.
		fmt.Fprintf(&references, "\n\t\t\tAND NOT EXISTS (SELECT 1 FROM %s WHERE %s = candidate.id)",
			pgx.Identifier{field.Table}.Sanitize(), pgx.Identifier{field.Column}.Sanitize())
	}
	statement := `DELETE FROM localized_texts WHERE id IN (
		SELECT candidate.id FROM localized_texts AS candidate
		WHERE candidate.tenant_id = current_setting('application.tenant', true)
			AND candidate.updated_at < now() - make_interval(secs => $1)` + references.String() + `
		ORDER BY candidate.updated_at
		LIMIT $2
		FOR UPDATE SKIP LOCKED)`
	tag, err := current.Exec(ctx, statement, olderThan.Seconds(), limit)
	if err != nil {
		return 0, fmt.Errorf("delete orphan localized texts: %w", err)
	}
	return tag.RowsAffected(), nil
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
	origin                *string
	locale                *string
	value                 *string
	status                *string
	translationSourceHash *string
	translatedAt          *time.Time
	sourceHash            string
	context               string
	sourceValue           string
	sourceLocale          string
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
			&scanned.origin, &scanned.status, &scanned.translationSourceHash, &scanned.translatedAt); err != nil {
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
	}
	if scanned.value != nil {
		translation.Value = *scanned.value
	}
	return translation, nil
}
