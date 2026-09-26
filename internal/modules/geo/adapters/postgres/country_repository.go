package postgres

import (
	"context"
	"fmt"

	"github.com/jackc/pgx/v5"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/i18n"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/application"
	"github.com/MathiasHilgert/frappe-api/internal/modules/geo/domain"
)

// CountryRepository is the Postgres application.CountryReader.
type CountryRepository struct {
	connection connection
}

var _ application.CountryReader = (*CountryRepository)(nil)

// NewCountryRepository returns a CountryRepository reading through pool.
func NewCountryRepository(pool PoolSource) *CountryRepository {
	return &CountryRepository{connection: connection{source: pool}}
}

// Countries returns a page of countries ordered by code.
func (repository *CountryRepository) Countries(ctx context.Context, locale i18n.Locale, filter application.CountryFilter) ([]domain.Country, error) {
	return repository.collect(repository.connection.query(ctx, listCountriesQuery, repository.connection.locale(locale), filter.After, filter.Limit))
}

// CountriesByCode returns the countries with codes.
func (repository *CountryRepository) CountriesByCode(ctx context.Context, locale i18n.Locale, codes []string) ([]domain.Country, error) {
	return repository.collect(repository.connection.query(ctx, countriesByCodeQuery, repository.connection.locale(locale), codes))
}

func (*CountryRepository) collect(rows pgx.Rows, err error) ([]domain.Country, error) {
	if err != nil {
		return nil, err
	}
	countryRows, err := pgx.CollectRows(rows, pgx.RowToStructByName[countryRow])
	if err != nil {
		return nil, fmt.Errorf("geo countries: %w", err)
	}
	countries := make([]domain.Country, 0, len(countryRows))
	for _, row := range countryRows {
		countries = append(countries, row.country())
	}
	return countries, nil
}
