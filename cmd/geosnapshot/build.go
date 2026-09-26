package main

import (
	"fmt"
	"io"
	"slices"
	"strconv"
	"strings"
)

// Place kinds, as stored in places.kind.
const (
	countryKind     = "country"
	subdivisionKind = "subdivision"
	cityKind        = "city"
)

// sources are the files a snapshot is built from.
type sources struct {
	countries      io.Reader // GeoNames countryInfo.txt
	subdivisions   io.Reader // GeoNames admin1CodesASCII.txt
	timeZones      io.Reader // GeoNames timeZones.txt
	cities         io.Reader // GeoNames cities500.txt (inside cities500.zip)
	alternateNames io.Reader // GeoNames alternateNamesV2.txt (inside alternateNamesV2.zip)
	// territoryNames holds one CLDR territories.json per CLDR territory
	// locale of cldrLocales, subdivisionNames one CLDR subdivisions XML per
	// CLDR subdivision locale.
	territoryNames       map[string]io.Reader
	subdivisionNames     map[string]io.Reader
	subdivisionValidity  io.Reader // CLDR common/validity/subdivision.xml
	wikidataCodes        io.Reader // committed Wikidata P300/P1566 snapshot
	subdivisionOverrides io.Reader // committed override file
}

// table is one snapshot file: rows in COPY column order, sorted.
type table struct {
	name string
	rows [][]string
}

// snapshot is every table, in load order (referenced tables first; the
// countries -> cities and countries -> time_zones references are deferred
// in the schema).
type snapshot struct {
	tables []table
}

// parsedSources holds every source, parsed.
type parsedSources struct {
	territoryNames   map[string]map[string]string // platform locale -> alpha-2 -> name
	subdivisionNames map[string]map[string]string // platform locale -> CLDR code -> name
	validCodes       map[string]bool
	wikidata         map[int64][]string
	overrides        map[string]subdivisionOverride
	countries        []country
	subdivisions     []subdivision
	timeZones        []timeZone
	cities           []city
}

// buildSnapshot filters and normalizes the sources into the snapshot
// tables. Rows are sorted by key, so the same sources always produce the
// same snapshot.
func buildSnapshot(input sources) (snapshot, error) {
	parsed, err := parseSources(input)
	if err != nil {
		return snapshot{}, err
	}

	countryCodes, subdivisions, cities, err := coveredEntities(parsed)
	if err != nil {
		return snapshot{}, err
	}

	isoCodes, err := resolveISOCodes(subdivisions, isoCodeSources{
		wikidata:     parsed.wikidata,
		overrides:    parsed.overrides,
		valid:        parsed.validCodes,
		englishNames: parsed.subdivisionNames["en"],
	})
	if err != nil {
		return snapshot{}, err
	}

	places, err := registerPlaces(parsed, subdivisions, cities, isoCodes)
	if err != nil {
		return snapshot{}, err
	}

	names, err := placeNames(input.alternateNames, parsed, subdivisions, cities, isoCodes, places)
	if err != nil {
		return snapshot{}, err
	}

	return snapshot{tables: []table{
		places.table(),
		countriesTable(parsed.countries, cities, parsed.timeZones),
		timeZonesTable(parsed.timeZones, countryCodes),
		subdivisionsTable(subdivisions, isoCodes),
		citiesTable(cities, subdivisions),
		names,
	}}, nil
}

// coveredEntities drops subdivisions and cities of unknown countries and
// checks every city's time zone exists.
func coveredEntities(parsed parsedSources) (map[string]bool, []subdivision, []city, error) {
	countryCodes := map[string]bool{}
	for _, entry := range parsed.countries {
		countryCodes[entry.code] = true
	}
	subdivisions := slices.DeleteFunc(parsed.subdivisions, func(entry subdivision) bool { return !countryCodes[entry.countryCode] })
	cities := slices.DeleteFunc(parsed.cities, func(entry city) bool { return !countryCodes[entry.countryCode] })
	timeZoneIdentifiers := map[string]bool{}
	for _, entry := range parsed.timeZones {
		timeZoneIdentifiers[entry.identifier] = true
	}
	for _, entry := range cities {
		if !timeZoneIdentifiers[entry.timeZone] {
			return nil, nil, nil, fmt.Errorf("city %d (%s) has time zone %q, missing from timeZones.txt", entry.geonamesID, entry.name, entry.timeZone)
		}
	}
	return countryCodes, subdivisions, cities, nil
}

// registerPlaces registers every country, subdivision and city once.
func registerPlaces(parsed parsedSources, subdivisions []subdivision, cities []city, isoCodes map[int64]string) (*placeRegistry, error) {
	places := newPlaceRegistry()
	for _, entry := range parsed.countries {
		places.add(entry.geonamesID, countryKind, countryName(entry, parsed.territoryNames["en"]))
	}
	for _, entry := range subdivisions {
		places.add(entry.geonamesID, subdivisionKind, subdivisionName(entry, isoCodes[entry.geonamesID], parsed.subdivisionNames["en"]))
	}
	for _, entry := range cities {
		places.add(entry.geonamesID, cityKind, entry.name)
	}
	return places, places.duplicatesError()
}

func parseSources(input sources) (parsedSources, error) {
	var parsed parsedSources
	var err error
	if parsed.countries, err = parseCountries(input.countries); err != nil {
		return parsedSources{}, err
	}
	if parsed.subdivisions, err = parseSubdivisions(input.subdivisions); err != nil {
		return parsedSources{}, err
	}
	if parsed.timeZones, err = parseTimeZones(input.timeZones); err != nil {
		return parsedSources{}, err
	}
	if parsed.cities, err = parseCities(input.cities, includeCity); err != nil {
		return parsedSources{}, err
	}
	if parsed.validCodes, err = parseSubdivisionValidity(input.subdivisionValidity); err != nil {
		return parsedSources{}, err
	}
	if parsed.wikidata, err = parseWikidataCodes(input.wikidataCodes); err != nil {
		return parsedSources{}, err
	}
	if parsed.overrides, err = parseSubdivisionOverrides(input.subdivisionOverrides); err != nil {
		return parsedSources{}, err
	}
	return parsed, parseCLDRNames(input, &parsed)
}

// parseCLDRNames reads the CLDR territory and subdivision names of every
// platform locale.
func parseCLDRNames(input sources, parsed *parsedSources) error {
	var err error
	parsed.territoryNames = map[string]map[string]string{}
	parsed.subdivisionNames = map[string]map[string]string{}
	for _, mapping := range cldrLocales {
		reader, found := input.territoryNames[mapping.territory]
		if !found {
			return fmt.Errorf("missing CLDR territories for %s", mapping.territory)
		}
		if parsed.territoryNames[mapping.locale], err = parseTerritoryNames(reader); err != nil {
			return fmt.Errorf("%s: %w", mapping.territory, err)
		}
		reader, found = input.subdivisionNames[mapping.subdivision]
		if !found {
			return fmt.Errorf("missing CLDR subdivisions for %s", mapping.subdivision)
		}
		if parsed.subdivisionNames[mapping.locale], err = parseSubdivisionNames(reader); err != nil {
			return fmt.Errorf("%s: %w", mapping.subdivision, err)
		}
	}
	return nil
}

// countryName is the CLDR English name, falling back to GeoNames.
func countryName(entry country, englishNames map[string]string) string {
	if name := englishNames[entry.code]; name != "" {
		return name
	}
	return entry.name
}

// subdivisionName is the CLDR English name of its ISO code (UTF-8, such as
// "Córdoba"), falling back to the GeoNames admin1 name.
func subdivisionName(entry subdivision, isoCode string, englishNames map[string]string) string {
	if isoCode != "" {
		if name := englishNames[cldrSubdivisionCode(isoCode)]; name != "" {
			return name
		}
	}
	return entry.name
}

// placeRegistry collects every place once, whatever its kind.
type placeRegistry struct {
	kinds      map[int64]string
	names      map[int64]string
	duplicates []string
}

func newPlaceRegistry() *placeRegistry {
	return &placeRegistry{kinds: map[int64]string{}, names: map[int64]string{}}
}

func (registry *placeRegistry) add(identifier int64, kind, name string) {
	if existing, found := registry.kinds[identifier]; found {
		registry.duplicates = append(registry.duplicates, fmt.Sprintf("GeoNames id %d is both a %s and a %s", identifier, existing, kind))
		return
	}
	registry.kinds[identifier] = kind
	registry.names[identifier] = name
}

func (registry *placeRegistry) duplicatesError() error {
	if len(registry.duplicates) == 0 {
		return nil
	}
	slices.Sort(registry.duplicates)
	return fmt.Errorf("duplicate places:\n%s", strings.Join(registry.duplicates, "\n"))
}

// table: id, kind, name.
func (registry *placeRegistry) table() table {
	rows := make([][]string, 0, len(registry.kinds))
	for identifier, kind := range registry.kinds {
		rows = append(rows, []string{formatIdentifier(identifier), kind, registry.names[identifier]})
	}
	return table{name: "places", rows: sortRows(rows, compareNumericFirstColumn)}
}

// placeNames builds place_names: CLDR names for countries and
// subdivisions, the best GeoNames alternate name for cities. A name equal
// to the place's own name is left out; readers fall back to it.
func placeNames(alternateNames io.Reader, parsed parsedSources, subdivisions []subdivision, cities []city,
	isoCodes map[int64]string, places *placeRegistry,
) (table, error) {
	names := cldrPlaceNames(parsed, subdivisions, isoCodes)
	cityNames, err := cityPlaceNames(alternateNames, cities)
	if err != nil {
		return table{}, err
	}
	names = append(names, cityNames...)

	rows := make([][]string, 0, len(names))
	for _, entry := range names {
		if entry.name == "" || entry.name == places.names[entry.geonamesID] {
			continue
		}
		rows = append(rows, []string{formatIdentifier(entry.geonamesID), entry.locale, entry.name})
	}
	slices.SortFunc(rows, func(left, right []string) int {
		if order := compareNumericFirstColumn(left, right); order != 0 {
			return order
		}
		return localeOrder(left[1]) - localeOrder(right[1])
	})
	return table{name: "place_names", rows: rows}, nil
}

// cldrPlaceNames returns the CLDR name of every country and every
// subdivision with an ISO code, in every platform locale.
func cldrPlaceNames(parsed parsedSources, subdivisions []subdivision, isoCodes map[int64]string) []localizedName {
	var names []localizedName
	for _, mapping := range cldrLocales {
		for _, entry := range parsed.countries {
			names = append(names, localizedName{geonamesID: entry.geonamesID, locale: mapping.locale, name: parsed.territoryNames[mapping.locale][entry.code]})
		}
		for _, entry := range subdivisions {
			if code := isoCodes[entry.geonamesID]; code != "" {
				names = append(names, localizedName{geonamesID: entry.geonamesID, locale: mapping.locale, name: parsed.subdivisionNames[mapping.locale][cldrSubdivisionCode(code)]})
			}
		}
	}
	return names
}

// cityPlaceNames returns the best GeoNames alternate name of every city
// per platform locale.
func cityPlaceNames(alternateNames io.Reader, cities []city) ([]localizedName, error) {
	cityNames := make(map[int64]string, len(cities))
	for _, entry := range cities {
		cityNames[entry.geonamesID] = entry.name
	}
	selector := newNameSelector()
	wanted := func(geonamesID int64) bool {
		_, isCity := cityNames[geonamesID]
		return isCity
	}
	if err := scanAlternateNames(alternateNames, wanted, selector.consider); err != nil {
		return nil, err
	}
	return selector.names(cityNames), nil
}

func formatIdentifier(identifier int64) string {
	return strconv.FormatInt(identifier, 10)
}

func nullable(value string) string {
	if value == "" {
		return nullValue
	}
	return value
}

func sortRows(rows [][]string, compare func(left, right []string) int) [][]string {
	slices.SortFunc(rows, compare)
	return rows
}

func compareNumericFirstColumn(left, right []string) int {
	leftValue, _ := strconv.ParseInt(left[0], 10, 64)
	rightValue, _ := strconv.ParseInt(right[0], 10, 64)
	switch {
	case leftValue < rightValue:
		return -1
	case leftValue > rightValue:
		return 1
	default:
		return 0
	}
}

func compareFirstColumn(left, right []string) int {
	return strings.Compare(left[0], right[0])
}

// capitalCity picks a country's capital among the covered cities: its
// national capital (GeoNames PPLC; the one named like countryInfo's
// capital when there are several, such as seats of government), else the
// most populous city named like countryInfo's capital. It returns false
// when no covered city qualifies.
func capitalCity(entry country, cities []city) (city, bool) {
	var capitals, named []city
	for _, candidate := range cities {
		if candidate.countryCode != entry.code {
			continue
		}
		if candidate.featureCode == capitalFeatureCode {
			capitals = append(capitals, candidate)
		}
		if entry.capital != "" && foldName(candidate.name) == foldName(entry.capital) {
			named = append(named, candidate)
		}
	}
	if len(capitals) > 1 {
		if preferred := slices.IndexFunc(capitals, func(candidate city) bool { return slices.ContainsFunc(named, sameCity(candidate)) }); preferred >= 0 {
			return capitals[preferred], true
		}
	}
	if len(capitals) > 0 {
		return mostPopulous(capitals), true
	}
	if len(named) > 0 {
		return mostPopulous(named), true
	}
	return city{}, false
}

func sameCity(target city) func(city) bool {
	return func(candidate city) bool { return candidate.geonamesID == target.geonamesID }
}

// mostPopulous returns the most populous city, the lowest id on a tie.
func mostPopulous(cities []city) city {
	best := cities[0]
	for _, candidate := range cities[1:] {
		if candidate.population > best.population ||
			(candidate.population == best.population && candidate.geonamesID < best.geonamesID) {
			best = candidate
		}
	}
	return best
}

// defaultTimeZone is the capital's time zone, else the most populous
// covered city's, else the country's only time zone; "" when none.
func defaultTimeZone(entry country, capital city, hasCapital bool, cities []city, timeZones []timeZone) string {
	if hasCapital {
		return capital.timeZone
	}
	var inCountry []city
	for _, candidate := range cities {
		if candidate.countryCode == entry.code {
			inCountry = append(inCountry, candidate)
		}
	}
	if len(inCountry) > 0 {
		return mostPopulous(inCountry).timeZone
	}
	var zones []string
	for _, zone := range timeZones {
		if zone.countryCode == entry.code {
			zones = append(zones, zone.identifier)
		}
	}
	if len(zones) == 1 {
		return zones[0]
	}
	return ""
}

// countriesTable: code, place_id, alpha3_code, numeric_code,
// continent_code, currency_code, capital_city_id, default_time_zone_id.
func countriesTable(countries []country, cities []city, timeZones []timeZone) table {
	rows := make([][]string, 0, len(countries))
	for _, entry := range countries {
		capital, hasCapital := capitalCity(entry, cities)
		capitalID := nullValue
		if hasCapital {
			capitalID = formatIdentifier(capital.geonamesID)
		}
		rows = append(rows, []string{
			entry.code, formatIdentifier(entry.geonamesID), entry.alpha3Code, strconv.Itoa(entry.numericCode),
			entry.continent, nullable(entry.currencyCode), capitalID,
			nullable(defaultTimeZone(entry, capital, hasCapital, cities, timeZones)),
		})
	}
	return table{name: "countries", rows: sortRows(rows, compareFirstColumn)}
}

// timeZonesTable: id, country_code, january_offset_hours,
// july_offset_hours, raw_offset_hours.
func timeZonesTable(timeZones []timeZone, countryCodes map[string]bool) table {
	rows := make([][]string, 0, len(timeZones))
	for _, entry := range timeZones {
		countryCode := nullValue
		if countryCodes[entry.countryCode] {
			countryCode = entry.countryCode
		}
		rows = append(rows, []string{entry.identifier, countryCode, entry.januaryOffset, entry.julyOffset, entry.rawOffset})
	}
	return table{name: "time_zones", rows: sortRows(rows, compareFirstColumn)}
}

// subdivisionsTable: place_id, country_code, iso_code,
// geonames_admin1_code.
func subdivisionsTable(subdivisions []subdivision, isoCodes map[int64]string) table {
	rows := make([][]string, 0, len(subdivisions))
	for _, entry := range subdivisions {
		rows = append(rows, []string{formatIdentifier(entry.geonamesID), entry.countryCode, nullable(isoCodes[entry.geonamesID]), entry.code})
	}
	return table{name: "subdivisions", rows: sortRows(rows, compareNumericFirstColumn)}
}

// citiesTable: place_id, country_code, subdivision_id, ascii_name,
// latitude, longitude, population, feature_code, time_zone_id.
func citiesTable(cities []city, subdivisions []subdivision) table {
	subdivisionByCode := make(map[string]int64, len(subdivisions))
	for _, entry := range subdivisions {
		subdivisionByCode[entry.countryCode+"."+entry.code] = entry.geonamesID
	}
	rows := make([][]string, 0, len(cities))
	for _, entry := range cities {
		subdivisionID := nullValue
		if identifier, found := subdivisionByCode[entry.countryCode+"."+entry.admin1Code]; found {
			subdivisionID = formatIdentifier(identifier)
		}
		rows = append(rows, []string{
			formatIdentifier(entry.geonamesID), entry.countryCode, subdivisionID, entry.asciiName,
			entry.latitude, entry.longitude, strconv.FormatInt(entry.population, 10), entry.featureCode, entry.timeZone,
		})
	}
	return table{name: "cities", rows: sortRows(rows, compareNumericFirstColumn)}
}
