package main

import (
	"bytes"
	"compress/gzip"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"slices"
	"strconv"
	"strings"
)

// nullValue marks a NULL field in a table row; it is written as COPY's \N.
// GeoNames text never contains a NUL byte, so it cannot clash with data.
const nullValue = "\x00"

// license is recorded in the manifest; see NOTICE.
const license = "GeoNames (https://www.geonames.org), licensed under CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/)"

// sources are the GeoNames dump files a snapshot is built from.
type sources struct {
	countries      io.Reader // countryInfo.txt
	subdivisions   io.Reader // admin1CodesASCII.txt
	timeZones      io.Reader // timeZones.txt
	cities         io.Reader // cities500.txt (inside cities500.zip)
	alternateNames io.Reader // alternateNamesV2.txt (inside alternateNamesV2.zip)
}

// table is one snapshot file: rows in COPY column order, sorted.
type table struct {
	name string
	rows [][]string
}

// snapshot is every table, in load order (referenced tables first).
type snapshot struct {
	tables []table
}

// buildSnapshot filters and normalizes the GeoNames sources into the
// snapshot tables. Rows are sorted by primary key, so the same sources
// always produce the same snapshot.
func buildSnapshot(input sources) (snapshot, error) {
	parsed, err := parseSources(input)
	if err != nil {
		return snapshot{}, err
	}
	countries, subdivisions, timeZones, cities := parsed.countries, parsed.subdivisions, parsed.timeZones, parsed.cities

	countryCodes := map[string]bool{}
	countryCodeByGeonamesID := map[int64]string{}
	countryNames := map[int64]string{}
	for _, entry := range countries {
		countryCodes[entry.code] = true
		countryCodeByGeonamesID[entry.geonamesID] = entry.code
		countryNames[entry.geonamesID] = entry.name
	}

	subdivisions = slices.DeleteFunc(subdivisions, func(entry subdivision) bool { return !countryCodes[entry.countryCode] })
	subdivisionByCode := map[string]int64{}
	subdivisionNames := map[int64]string{}
	for _, entry := range subdivisions {
		subdivisionByCode[entry.countryCode+"."+entry.code] = entry.geonamesID
		subdivisionNames[entry.geonamesID] = entry.name
	}

	timeZoneIdentifiers := map[string]bool{}
	for _, entry := range timeZones {
		timeZoneIdentifiers[entry.identifier] = true
	}

	cities = slices.DeleteFunc(cities, func(entry city) bool { return !countryCodes[entry.countryCode] })
	cityNames := map[int64]string{}
	for _, entry := range cities {
		if !timeZoneIdentifiers[entry.timeZone] {
			return snapshot{}, fmt.Errorf("city %d (%s) has time zone %q, missing from timeZones.txt", entry.geonamesID, entry.name, entry.timeZone)
		}
		cityNames[entry.geonamesID] = entry.name
	}

	selector := newNameSelector()
	wanted := func(geonamesID int64) bool {
		_, isCountry := countryNames[geonamesID]
		_, isSubdivision := subdivisionNames[geonamesID]
		_, isCity := cityNames[geonamesID]
		return isCountry || isSubdivision || isCity
	}
	if err := scanAlternateNames(input.alternateNames, wanted, selector.consider); err != nil {
		return snapshot{}, err
	}

	return snapshot{tables: []table{
		countriesTable(countries),
		timeZonesTable(timeZones, countryCodes),
		subdivisionsTable(subdivisions),
		citiesTable(cities, subdivisionByCode),
		namesTable("country_names", selector.names(countryNames), func(geonamesID int64) string {
			return countryCodeByGeonamesID[geonamesID]
		}),
		namesTable("subdivision_names", selector.names(subdivisionNames), formatIdentifier),
		namesTable("city_names", selector.names(cityNames), formatIdentifier),
	}}, nil
}

// parsedSources holds the small source files, parsed, and the covered
// cities.
type parsedSources struct {
	countries    []country
	subdivisions []subdivision
	timeZones    []timeZone
	cities       []city
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
	return parsed, nil
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

// countriesTable: code, alpha3_code, numeric_code, geonames_id, name,
// continent_code, currency_code.
func countriesTable(countries []country) table {
	rows := make([][]string, 0, len(countries))
	for _, entry := range countries {
		rows = append(rows, []string{
			entry.code, entry.alpha3Code, strconv.Itoa(entry.numericCode), formatIdentifier(entry.geonamesID),
			entry.name, entry.continent, nullable(entry.currencyCode),
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

// subdivisionsTable: id, country_code, code, name, ascii_name.
func subdivisionsTable(subdivisions []subdivision) table {
	rows := make([][]string, 0, len(subdivisions))
	for _, entry := range subdivisions {
		rows = append(rows, []string{formatIdentifier(entry.geonamesID), entry.countryCode, entry.code, entry.name, entry.asciiName})
	}
	return table{name: "subdivisions", rows: sortRows(rows, compareNumericFirstColumn)}
}

// citiesTable: id, country_code, subdivision_id, name, ascii_name,
// latitude, longitude, population, feature_code, time_zone_id.
func citiesTable(cities []city, subdivisionByCode map[string]int64) table {
	rows := make([][]string, 0, len(cities))
	for _, entry := range cities {
		subdivisionID := nullValue
		if identifier, found := subdivisionByCode[entry.countryCode+"."+entry.admin1Code]; found {
			subdivisionID = formatIdentifier(identifier)
		}
		rows = append(rows, []string{
			formatIdentifier(entry.geonamesID), entry.countryCode, subdivisionID, entry.name, entry.asciiName,
			entry.latitude, entry.longitude, strconv.FormatInt(entry.population, 10), entry.featureCode, entry.timeZone,
		})
	}
	return table{name: "cities", rows: sortRows(rows, compareNumericFirstColumn)}
}

// namesTable: <entity key>, locale, name. names is already sorted by
// GeoNames identifier then locale; country keys are re-sorted by code.
func namesTable(name string, names []localizedName, entityKey func(int64) string) table {
	rows := make([][]string, 0, len(names))
	for _, entry := range names {
		rows = append(rows, []string{entityKey(entry.geonamesID), entry.locale, entry.name})
	}
	slices.SortStableFunc(rows, func(left, right []string) int {
		if len(left[0]) == len(right[0]) {
			return strings.Compare(left[0], right[0])
		}
		return len(left[0]) - len(right[0])
	})
	return table{name: name, rows: rows}
}

// escapeCopyField escapes value for PostgreSQL's COPY text format.
func escapeCopyField(value string) string {
	if value == nullValue {
		return `\N`
	}
	return strings.NewReplacer(`\`, `\\`, "\t", `\t`, "\n", `\n`, "\r", `\r`).Replace(value)
}

// sourceFile is one GeoNames dump file a snapshot was built from.
type sourceFile struct {
	name   string
	sha256 string
}

// sourceDescription records where a snapshot came from.
type sourceDescription struct {
	dumpDate string
	files    []sourceFile
}

type manifestSource struct {
	File   string `json:"file"`
	SHA256 string `json:"sha256"`
}

type manifestTable struct {
	File   string `json:"file"`
	SHA256 string `json:"sha256"`
	Rows   int    `json:"rows"`
}

// manifest describes a written snapshot: its origin, license and, per
// table, the row count and the SHA-256 of the compressed file.
type manifest struct {
	Source   string           `json:"source"`
	License  string           `json:"license"`
	DumpDate string           `json:"dumpDate"`
	Sources  []manifestSource `json:"sources"`
	Tables   []manifestTable  `json:"tables"`
}

// writeSnapshot writes every table as "<name>.tsv.gz" (COPY text format,
// gzip without a timestamp, so identical rows give identical bytes) and
// manifest.json into directory.
func writeSnapshot(directory string, built snapshot, source sourceDescription) error {
	if err := os.MkdirAll(directory, 0o750); err != nil {
		return fmt.Errorf("create snapshot directory: %w", err)
	}
	written := manifest{
		Source:   "https://download.geonames.org/export/dump/",
		License:  license,
		DumpDate: source.dumpDate,
	}
	for _, file := range source.files {
		written.Sources = append(written.Sources, manifestSource{File: file.name, SHA256: file.sha256})
	}
	for _, entry := range built.tables {
		file := entry.name + ".tsv.gz"
		content, err := compressTable(entry)
		if err != nil {
			return fmt.Errorf("compress %s: %w", file, err)
		}
		if err := os.WriteFile(filepath.Join(directory, file), content, 0o600); err != nil {
			return fmt.Errorf("write %s: %w", file, err)
		}
		digest := sha256.Sum256(content)
		written.Tables = append(written.Tables, manifestTable{File: file, Rows: len(entry.rows), SHA256: hex.EncodeToString(digest[:])})
	}
	encoded, err := json.MarshalIndent(written, "", "  ")
	if err != nil {
		return fmt.Errorf("encode manifest: %w", err)
	}
	if err := os.WriteFile(filepath.Join(directory, "manifest.json"), append(encoded, '\n'), 0o600); err != nil {
		return fmt.Errorf("write manifest: %w", err)
	}
	return nil
}

func compressTable(entry table) ([]byte, error) {
	var buffer bytes.Buffer
	writer, err := gzip.NewWriterLevel(&buffer, gzip.BestCompression)
	if err != nil {
		return nil, err
	}
	for _, row := range entry.rows {
		fields := make([]string, len(row))
		for index, value := range row {
			fields[index] = escapeCopyField(value)
		}
		if _, err := io.WriteString(writer, strings.Join(fields, "\t")+"\n"); err != nil {
			return nil, err
		}
	}
	if err := writer.Close(); err != nil {
		return nil, err
	}
	return buffer.Bytes(), nil
}
