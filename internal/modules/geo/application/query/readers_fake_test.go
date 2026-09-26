package query_test

import (
	"context"
	"errors"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

var (
	spanish       = i18n.MustParseLocale("es-419")
	errDatabase   = errors.New("database down")
	argentinaCode = "AR"
)

// fakeReaders implements every geo port on in-memory data and records how
// it was called.
type fakeReaders struct {
	countries     map[string]domain.Country
	calls         map[string]int
	err           error
	revisionError error
	locale        i18n.Locale
	revision      string
	lastCodes     []string
	countryFilter application.CountryFilter
}

func newFakeReaders() *fakeReaders {
	currency := "ARS"
	return &fakeReaders{
		calls: map[string]int{},
		countries: map[string]domain.Country{
			"AR": {Code: "AR", PlaceID: 3865483, Name: "Argentina", CurrencyCode: &currency},
		},
	}
}

func (readers *fakeReaders) Countries(_ context.Context, locale i18n.Locale, filter application.CountryFilter) ([]domain.Country, error) {
	readers.calls["Countries"]++
	readers.locale, readers.countryFilter = locale, filter
	if readers.err != nil {
		return nil, readers.err
	}
	return []domain.Country{readers.countries["AR"]}, nil
}

func (readers *fakeReaders) CountriesByCode(_ context.Context, locale i18n.Locale, codes []string) ([]domain.Country, error) {
	readers.calls["CountriesByCode"]++
	readers.locale, readers.lastCodes = locale, codes
	if readers.err != nil {
		return nil, readers.err
	}
	var result []domain.Country
	for _, code := range codes {
		if country, found := readers.countries[code]; found {
			result = append(result, country)
		}
	}
	return result, nil
}

func (readers *fakeReaders) DataRevision(context.Context) (string, error) {
	readers.calls["DataRevision"]++
	return readers.revision, readers.revisionError
}
