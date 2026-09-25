package main

import (
	"bytes"
	"compress/gzip"
	"encoding/json"
	"io"
	"os"
	"path/filepath"
	"slices"
	"strings"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
)

// The fixtures below follow the exact GeoNames dump formats documented in
// https://download.geonames.org/export/dump/readme.txt, trimmed to a few
// rows: Argentina (Latin America, low population threshold) and Germany
// (rest of the world, high population threshold).

const countryInfoFixture = "# GeoNames country information\n" +
	"#ISO\tISO3\tISO-Numeric\tfips\tCountry\tCapital\tArea(in sq km)\tPopulation\tContinent\ttld\tCurrencyCode\tCurrencyName\tPhone\tPostal Code Format\tPostal Code Regex\tLanguages\tgeonameid\tneighbours\tEquivalentFipsCode\n" +
	"AR\tARG\t032\tAR\tArgentina\tBuenos Aires\t2766890\t44494502\tSA\t.ar\tARS\tPeso\t54\t@####@@@\t\tes-AR,en,it,de,fr,gn\t3865483\tCL,BO,UY,PY,BR\t\n" +
	"AQ\tATA\t010\tAY\tAntarctica\t\t14000000\t0\tAN\t.aq\t\t\t\t\t\t\t6697173\t\t\n" +
	"DE\tDEU\t276\tGM\tGermany\tBerlin\t357021\t82927922\tEU\t.de\tEUR\tEuro\t49\t#####\t\tde\t2921044\tCH,PL,NL,DK,BE,CZ,LU,FR,AT\t\n"

const admin1Fixture = "AR.05\tCórdoba\tCordoba\t3860255\n" +
	"DE.16\tBerlin\tBerlin\t2950157\n" +
	"XX.01\tNowhere\tNowhere\t1\n"

const timeZonesFixture = "CountryCode\tTimeZoneId\tGMT offset 1. Jan 2026\tDST offset 1. Jul 2026\trawOffset (independent of DST)\n" +
	"AR\tAmerica/Argentina/Cordoba\t-3.0\t-3.0\t-3.0\n" +
	"DE\tEurope/Berlin\t1.0\t2.0\t1.0\n"

// cityRow builds one geoname table row (19 tab separated columns).
func cityRow(identifier, name, asciiName, featureCode, countryCode, admin1Code, population, timeZone string) string {
	return strings.Join([]string{
		identifier, name, asciiName, "", "-31.97", "-64.56", "P", featureCode, countryCode, "",
		admin1Code, "", "", "", population, "", "800", timeZone, "2024-01-01",
	}, "\t") + "\n"
}

var citiesFixture = cityRow("3832734", "Villa General Belgrano", "Villa General Belgrano", "PPL", "AR", "05", "5888", "America/Argentina/Cordoba") +
	cityRow("3860259", "Córdoba", "Cordoba", "PPLA", "AR", "05", "1428214", "America/Argentina/Cordoba") +
	cityRow("1000001", "Tiny Latin Village", "Tiny Latin Village", "PPL", "AR", "05", "500", "America/Argentina/Cordoba") +
	cityRow("1000002", "Unknown Province Town", "Unknown Province Town", "PPL", "AR", "99", "900", "America/Argentina/Cordoba") +
	cityRow("1000003", "Palermo Chico", "Palermo Chico", "PPLX", "AR", "05", "20000", "America/Argentina/Cordoba") +
	cityRow("2950159", "Berlin", "Berlin", "PPLC", "DE", "16", "3426354", "Europe/Berlin") +
	cityRow("1000004", "Small German Town", "Small German Town", "PPL", "DE", "16", "14000", "Europe/Berlin") +
	cityRow("1000005", "Tiny Capital", "Tiny Capital", "PPLC", "DE", "16", "300", "Europe/Berlin")

// alternateRow builds one alternateNamesV2 row: alternateNameId,
// geonameid, isolanguage, alternate name, isPreferredName, isShortName,
// isColloquial, isHistoric, from, to.
func alternateRow(identifier, geonamesID, language, name, preferred, short, colloquial, historic, to string) string {
	return strings.Join([]string{identifier, geonamesID, language, name, preferred, short, colloquial, historic, "", to}, "\t") + "\n"
}

var alternateNamesFixture = alternateRow("1", "3832734", "ja", "ヴィラ・ヘネラル・ベルグラーノ", "", "", "", "", "") +
	alternateRow("2", "3860255", "pt", "Córdoba (província)", "", "", "", "", "") +
	alternateRow("3", "3860255", "pt-BR", "Província de Córdoba", "", "", "", "", "") +
	alternateRow("4", "3860255", "en", "Cordoba Province", "", "", "", "", "") +
	alternateRow("5", "3860255", "en", "Córdoba Province", "1", "", "", "", "") +
	alternateRow("6", "3865483", "de", "Argentinien", "", "", "", "", "") +
	alternateRow("7", "3865483", "en", "Argentina", "1", "", "", "", "") +
	alternateRow("8", "3865483", "es", "República Argentina", "", "", "", "", "") +
	alternateRow("9", "3865483", "es", "Argentina", "1", "1", "", "", "") +
	alternateRow("10", "3865483", "fr", "Argentine", "", "", "", "", "") +
	alternateRow("11", "3865483", "fr", "Ancienne Argentine", "1", "", "", "1", "") +
	alternateRow("12", "3865483", "it", "Argentina Slang", "1", "", "1", "", "") +
	alternateRow("13", "3865483", "it", "Argentina Vecchia", "1", "", "", "", "1990") +
	alternateRow("14", "3865483", "zh", "阿根廷", "", "", "", "", "") +
	alternateRow("15", "3865483", "zh-Hant", "阿根廷共和國", "1", "", "", "", "") +
	alternateRow("16", "2950159", "ru", "Берлин", "", "", "", "", "") +
	alternateRow("17", "1000004", "ru", "Not Covered", "", "", "", "", "") +
	alternateRow("18", "3865483", "link", "https://en.wikipedia.org/wiki/Argentina", "", "", "", "", "") +
	alternateRow("19", "3865483", "ko", "아르헨티나", "", "", "", "", "")

func fixtureSources() sources {
	return sources{
		countries:      strings.NewReader(countryInfoFixture),
		subdivisions:   strings.NewReader(admin1Fixture),
		timeZones:      strings.NewReader(timeZonesFixture),
		cities:         strings.NewReader(citiesFixture),
		alternateNames: strings.NewReader(alternateNamesFixture),
	}
}

func buildFixture(t *testing.T) snapshot {
	t.Helper()
	built, err := buildSnapshot(fixtureSources())
	if err != nil {
		t.Fatalf("build snapshot: %v", err)
	}
	return built
}

func tableRows(t *testing.T, built snapshot, name string) [][]string {
	t.Helper()
	for _, candidate := range built.tables {
		if candidate.name == name {
			return candidate.rows
		}
	}
	t.Fatalf("table %q missing from snapshot", name)
	return nil
}

func firstColumn(rows [][]string) []string {
	values := make([]string, 0, len(rows))
	for _, row := range rows {
		values = append(values, row[0])
	}
	return values
}

func TestBuildSnapshotKeepsEveryCountryAndKnownSubdivision(t *testing.T) {
	built := buildFixture(t)

	if got, want := firstColumn(tableRows(t, built, "countries")), []string{"AQ", "AR", "DE"}; !slices.Equal(got, want) {
		t.Errorf("countries = %v, want %v", got, want)
	}
	antarctica := tableRows(t, built, "countries")[0]
	if antarctica[6] != nullValue {
		t.Errorf("Antarctica currency = %q, want NULL", antarctica[6])
	}
	// XX.01 belongs to no known country and is dropped.
	if got, want := firstColumn(tableRows(t, built, "subdivisions")), []string{"2950157", "3860255"}; !slices.Equal(got, want) {
		t.Errorf("subdivisions = %v, want %v", got, want)
	}
}

func TestBuildSnapshotFiltersCitiesByRegionalPopulationThreshold(t *testing.T) {
	built := buildFixture(t)

	got := firstColumn(tableRows(t, built, "cities"))
	// Kept: Latin American towns above 500 (even without a known
	// subdivision), cities above 15000 elsewhere, and national capitals.
	// Dropped: exactly 500 in Latin America, 14000 elsewhere, and PPLX
	// sections of a populated place.
	want := []string{"1000002", "1000005", "2950159", "3832734", "3860259"}
	if !slices.Equal(got, want) {
		t.Errorf("cities = %v, want %v", got, want)
	}
}

func TestBuildSnapshotResolvesCitySubdivisionAndTimeZone(t *testing.T) {
	built := buildFixture(t)

	for _, row := range tableRows(t, built, "cities") {
		switch row[0] {
		case "3832734":
			want := []string{"3832734", "AR", "3860255", "Villa General Belgrano", "Villa General Belgrano", "-31.97", "-64.56", "5888", "PPL", "America/Argentina/Cordoba"}
			if !slices.Equal(row, want) {
				t.Errorf("Villa General Belgrano row = %v, want %v", row, want)
			}
		case "1000002":
			if row[2] != nullValue {
				t.Errorf("unknown admin1 code subdivision = %q, want NULL", row[2])
			}
		}
	}
}

func TestBuildSnapshotPicksBestAlternateNamePerLocale(t *testing.T) {
	built := buildFixture(t)

	names := map[string]string{}
	for _, row := range tableRows(t, built, "country_names") {
		names[row[0]+" "+row[1]] = row[2]
	}
	for _, row := range tableRows(t, built, "subdivision_names") {
		names[row[0]+" "+row[1]] = row[2]
	}
	for _, row := range tableRows(t, built, "city_names") {
		names[row[0]+" "+row[1]] = row[2]
	}

	want := map[string]string{
		// Preferred and short wins over a longer official name; es-419
		// reads GeoNames "es". Equal to the original name, so not stored:
		// "AR es-419" is absent below.
		"AR de":      "Argentinien",
		"AR zh-Hans": "阿根廷",
		"AR ko":      "아르헨티나",
		// Historic, colloquial and ended names are never picked, so fr
		// falls back to the plain name and it has no name at all.
		"AR fr": "Argentine",
		// pt-BR prefers the Brazilian variant over generic "pt"; en
		// prefers the preferred name.
		"3860255 pt-BR": "Província de Córdoba",
		"3860255 en":    "Córdoba Province",
		"3832734 ja":    "ヴィラ・ヘネラル・ベルグラーノ",
		"2950159 ru":    "Берлин",
	}
	for key, value := range want {
		if names[key] != value {
			t.Errorf("name %s = %q, want %q", key, names[key], value)
		}
	}
	for _, absent := range []string{"AR es-419", "AR en", "AR it", "1000004 ru"} {
		if value, found := names[absent]; found {
			t.Errorf("name %s = %q, want none", absent, value)
		}
	}
	if len(names) != len(want) {
		t.Errorf("got %d names, want %d: %v", len(names), len(want), names)
	}
}

func TestBuildSnapshotKeepsTimeZones(t *testing.T) {
	built := buildFixture(t)

	want := [][]string{
		{"America/Argentina/Cordoba", "AR", "-3.0", "-3.0", "-3.0"},
		{"Europe/Berlin", "DE", "1.0", "2.0", "1.0"},
	}
	if got := tableRows(t, built, "time_zones"); !slices.EqualFunc(got, want, slices.Equal) {
		t.Errorf("time_zones = %v, want %v", got, want)
	}
}

func TestBuildSnapshotRejectsCityWithUnknownTimeZone(t *testing.T) {
	fixture := fixtureSources()
	fixture.cities = strings.NewReader(cityRow("1", "Lost", "Lost", "PPL", "AR", "05", "9000", "Mars/Olympus_Mons"))

	if _, err := buildSnapshot(fixture); err == nil {
		t.Fatal("build snapshot with an unknown city time zone: want error, got nil")
	}
}

func TestLocaleLanguagesCoverTheDefaultSupportedLocales(t *testing.T) {
	covered := make([]string, 0, len(localeLanguages))
	for _, mapping := range localeLanguages {
		covered = append(covered, mapping.locale)
	}
	for _, locale := range strings.Split(configuration.DefaultSupportedLocales, ",") {
		if !slices.Contains(covered, locale) {
			t.Errorf("supported locale %q has no GeoNames language mapping", locale)
		}
	}
}

func TestEscapeCopyFieldEscapesCopyTextSpecialCharacters(t *testing.T) {
	got := escapeCopyField("a\\b\tc\nd\re")
	if want := `a\\b\tc\nd\re`; got != want {
		t.Errorf("escapeCopyField = %q, want %q", got, want)
	}
}

func TestWriteSnapshotIsDeterministic(t *testing.T) {
	built := buildFixture(t)
	first, second := t.TempDir(), t.TempDir()
	source := sourceDescription{dumpDate: "2026-09-25", files: []sourceFile{{name: "countryInfo.txt", sha256: "abc"}}}

	if err := writeSnapshot(first, built, source); err != nil {
		t.Fatalf("write first snapshot: %v", err)
	}
	if err := writeSnapshot(second, built, source); err != nil {
		t.Fatalf("write second snapshot: %v", err)
	}

	entries, err := os.ReadDir(first)
	if err != nil {
		t.Fatalf("read snapshot directory: %v", err)
	}
	if len(entries) != len(built.tables)+1 {
		t.Fatalf("snapshot has %d files, want %d tables plus the manifest", len(entries), len(built.tables))
	}
	for _, entry := range entries {
		left, _ := os.ReadFile(filepath.Join(first, entry.Name()))
		right, _ := os.ReadFile(filepath.Join(second, entry.Name()))
		if !bytes.Equal(left, right) {
			t.Errorf("%s differs between two writes of the same snapshot", entry.Name())
		}
	}
}

func TestWriteSnapshotWritesCopyTextAndManifest(t *testing.T) {
	built := buildFixture(t)
	directory := t.TempDir()
	source := sourceDescription{dumpDate: "2026-09-25", files: []sourceFile{{name: "countryInfo.txt", sha256: "abc"}}}

	if err := writeSnapshot(directory, built, source); err != nil {
		t.Fatalf("write snapshot: %v", err)
	}

	compressed, err := os.Open(filepath.Join(directory, "countries.tsv.gz"))
	if err != nil {
		t.Fatalf("open countries: %v", err)
	}
	defer func() { _ = compressed.Close() }()
	reader, err := gzip.NewReader(compressed)
	if err != nil {
		t.Fatalf("gunzip countries: %v", err)
	}
	content, err := io.ReadAll(reader)
	if err != nil {
		t.Fatalf("read countries: %v", err)
	}
	if !strings.HasPrefix(string(content), "AQ\tATA\t10\t6697173\tAntarctica\tAN\t\\N\n") {
		t.Errorf("countries.tsv starts with %q", strings.SplitN(string(content), "\n", 2)[0])
	}

	manifestContent, err := os.ReadFile(filepath.Join(directory, "manifest.json"))
	if err != nil {
		t.Fatalf("read manifest: %v", err)
	}
	var parsed manifest
	if err := json.Unmarshal(manifestContent, &parsed); err != nil {
		t.Fatalf("parse manifest: %v", err)
	}
	if parsed.DumpDate != "2026-09-25" || parsed.License != license || len(parsed.Sources) != 1 {
		t.Errorf("manifest = %+v", parsed)
	}
	for _, table := range parsed.Tables {
		if table.File == "countries.tsv.gz" && table.Rows != 3 {
			t.Errorf("manifest countries rows = %d, want 3", table.Rows)
		}
		if len(table.SHA256) != 64 {
			t.Errorf("manifest %s sha256 = %q", table.File, table.SHA256)
		}
	}
}
