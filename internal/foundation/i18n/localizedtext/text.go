package localizedtext

import (
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"fmt"
	"regexp"
	"time"

	"github.com/google/uuid"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
)

var (
	// ErrNotFound reports a text that does not exist or that the current
	// tenant cannot see.
	ErrNotFound = errors.New("localizedtext: text not found")
	// ErrEmptyValue reports an empty source or translation value.
	ErrEmptyValue = errors.New("localizedtext: value must not be empty")
	// ErrUnsupportedLocale reports a locale the platform does not support.
	ErrUnsupportedLocale = errors.New("localizedtext: unsupported locale")
	// ErrSourceLocale reports a translation into the text's own source
	// locale; change the source with UpdateSource instead.
	ErrSourceLocale = errors.New("localizedtext: cannot translate a text into its source locale")
	// ErrInvalidField reports a malformed or conflicting Field declaration.
	ErrInvalidField = errors.New("localizedtext: invalid field")
	// ErrNoFields reports an orphan sweep with no declared field: with
	// nothing referencing texts, every text would look orphaned.
	ErrNoFields = errors.New("localizedtext: no field declared, refusing to delete orphans")
)

// ID identifies a localized text. Entities store it in a
// "<field>_text_id uuid" column; convert with ID(value) and uuid.UUID(id).
type ID uuid.UUID

// NewID returns a new random (version 4) ID.
func NewID() ID {
	return ID(uuid.New())
}

// ParseID parses the canonical textual form of an ID.
func ParseID(value string) (ID, error) {
	parsed, err := uuid.Parse(value)
	if err != nil {
		return ID{}, fmt.Errorf("localizedtext: parse id %q: %w", value, err)
	}
	return ID(parsed), nil
}

// String returns the canonical textual form of id.
func (id ID) String() string {
	return uuid.UUID(id).String()
}

// IsZero reports whether id is the zero ID.
func (id ID) IsZero() bool {
	return id == ID{}
}

// Origin says who wrote a value.
type Origin string

const (
	// OriginSource marks the source value itself (only in Localized).
	OriginSource Origin = "source"
	// OriginManual marks a translation written by a person. A machine
	// translation never overwrites it.
	OriginManual Origin = "manual"
	// OriginMachine marks a translation produced by the machine
	// translator.
	OriginMachine Origin = "machine"
)

// Status is the freshness of a translation.
type Status string

const (
	// StatusCurrent marks a translation of the current source.
	StatusCurrent Status = "current"
	// StatusStale marks a translation of an older source: the source
	// changed after it was translated.
	StatusStale Status = "stale"
	// StatusPending marks a requested machine translation that has not
	// arrived yet. Its Value is empty, or the previous machine value
	// while it is being regenerated.
	StatusPending Status = "pending"
)

// Reason says why a translation is requested.
type Reason string

const (
	// ReasonMissing requests a translation that does not exist yet.
	ReasonMissing Reason = "missing"
	// ReasonStale requests a machine translation of a changed source.
	ReasonStale Reason = "stale"
)

// Translation is one translation of a text into one locale.
type Translation struct {
	Locale       i18n.Locale
	TranslatedAt time.Time
	Value        string
	Origin       Origin
	Status       Status
	SourceHash   string
}

// Text is a stored localized text with (some of) its translations.
type Text struct {
	SourceLocale i18n.Locale
	CreatedAt    time.Time
	UpdatedAt    time.Time
	SourceValue  string
	SourceHash   string
	// Context is the text's own machine translation hint; empty means the
	// referencing Field's default Context applies.
	Context      string
	Translations []Translation
	ID           ID
}

// Translation returns the translation into locale, if the text has one.
func (text Text) Translation(locale i18n.Locale) (Translation, bool) {
	for _, translation := range text.Translations {
		if translation.Locale == locale {
			return translation, true
		}
	}
	return Translation{}, false
}

// Source is what a module author writes: the source value, its locale
// (zero means the platform source locale) and an optional per-text
// machine translation hint overriding the Field's default Context.
type Source struct {
	Value   string
	Locale  i18n.Locale
	Context string
}

// Localized is a text resolved for one requested locale.
type Localized struct {
	Locale    i18n.Locale
	Requested i18n.Locale
	Value     string
	Origin    Origin
	Status    Status
	ID        ID
	Fallback  bool
}

// TranslationRequest asks the machine translator to translate a text.
type TranslationRequest struct {
	Locale     i18n.Locale
	SourceHash string
	Context    string
	Reason     Reason
	TextID     ID
}

// Hash returns the hex SHA-256 of a source: its locale and value,
// separated by a zero byte. A translation made for another hash is stale,
// so changing the source locale also marks every translation stale.
func Hash(locale i18n.Locale, value string) string {
	digest := sha256.Sum256([]byte(locale.String() + "\x00" + value))
	return hex.EncodeToString(digest[:])
}

// identifierPattern accepts plain lowercase SQL identifiers only, so a
// Field can be spliced into the orphan sweep safely.
var identifierPattern = regexp.MustCompile(`^[a-z_][a-z0-9_]{0,62}$`)

// Field declares one entity column that references localized texts,
// such as dishes.description_text_id, and the default machine translation
// Context for its texts. Declare every such column with Service.Field:
// the orphan sweep deletes texts no declared column references.
type Field struct {
	Table   string
	Column  string
	Context string
}

func (field Field) validate() error {
	if !identifierPattern.MatchString(field.Table) || !identifierPattern.MatchString(field.Column) {
		return fmt.Errorf("%w: table %q and column %q must be lowercase SQL identifiers", ErrInvalidField, field.Table, field.Column)
	}
	return nil
}
