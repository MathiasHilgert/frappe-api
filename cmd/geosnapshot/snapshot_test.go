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

// The fixtures below follow the exact source formats: GeoNames
// (https://download.geonames.org/export/dump/readme.txt), cldr-json
// territories.json, CLDR subdivisions and validity XML, and the committed
// Wikidata and override files. Argentina (Latin America, low population
// threshold) and Germany (rest of the world, high threshold).

const countryInfoFixture = "# GeoNames country information\n" +
	"#ISO\tISO3\tISO-Numeric\tfips\tCountry\tCapital\tArea(in sq km)\tPopulation\tContinent\ttld\tCurrencyCode\tCurrencyName\tPhone\tPostal Code Format\tPostal Code Regex\tLanguages\tgeonameid\tneighbours\tEquivalentFipsCode\n" +
	"AR\tARG\t032\tAR\tArgentina\tBuenos Aires\t2766890\t44494502\tSA\t.ar\tARS\tPeso\t54\t@####@@@\t\tes-AR,en,it,de,fr,gn\t3865483\tCL,BO,UY,PY,BR\t\n" +
	"AQ\tATA\t010\tAY\tAntarctica\t\t14000000\t0\tAN\t.aq\t\t\t\t\t\t\t6697173\t\t\n" +
	"DE\tDEU\t276\tGM\tGermany\tBerlin\t357021\t82927922\tEU\t.de\tEUR\tEuro\t49\t#####\t\tde\t2921044\tCH,PL,NL,DK,BE,CZ,LU,FR,AT\t\n"

const admin1Fixture = "AR.05\tCordoba\tCordoba\t3860255\n" +
	"AR.07\tBuenos Aires F.D.\tBuenos Aires F.D.\t3433955\n" +
	"DE.16\tBerlin\tBerlin\t2950157\n" +
	"DE.99\tNowhere Land\tNowhere Land\t9999\n" +
	"XX.01\tNowhere\tNowhere\t1\n"

const timeZonesFixture = "CountryCode\tTimeZoneId\tGMT offset 1. Jan 2026\tDST offset 1. Jul 2026\trawOffset (independent of DST)\n" +
	"AR\tAmerica/Argentina/Buenos_Aires\t-3.0\t-3.0\t-3.0\n" +
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
	cityRow("3435910", "Buenos Aires", "Buenos Aires", "PPLC", "AR", "07", "13076300", "America/Argentina/Buenos_Aires") +
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
	alternateRow("2", "3860259", "pt", "Córdova (cidade)", "", "", "", "", "") +
	alternateRow("3", "3860259", "pt-BR", "Córdoba (Argentina)", "", "", "", "", "") +
	alternateRow("4", "3860259", "en", "Cordoba City", "", "", "", "", "") +
	alternateRow("5", "3860259", "en", "Cordoba", "1", "", "", "", "") +
	alternateRow("6", "3860259", "fr", "Cordoue", "", "1", "", "", "") +
	alternateRow("7", "3860259", "fr", "Cordoue Ancienne", "1", "", "", "1", "") +
	alternateRow("8", "3860259", "it", "Cordova Slang", "1", "", "1", "", "") +
	alternateRow("9", "3860259", "it", "Cordova Vecchia", "1", "", "", "", "1990") +
	alternateRow("10", "3860259", "zh", "哥多華", "1", "", "", "", "") +
	alternateRow("11", "3860259", "zh-Hant", "科爾多瓦", "1", "", "", "", "") +
	alternateRow("12", "2950159", "zh", "柏林", "", "", "", "", "") +
	alternateRow("13", "2950159", "zh-CN", "柏林市", "", "", "", "", "") +
	alternateRow("14", "2950159", "ru", "Берлин", "", "", "", "", "") +
	alternateRow("15", "1000004", "ru", "Not Covered", "", "", "", "", "") +
	alternateRow("16", "3865483", "ja", "Country From GeoNames", "1", "", "", "", "") +
	alternateRow("17", "3860259", "link", "https://en.wikipedia.org/wiki/Cordoba", "", "", "", "", "")

// territoriesFixture renders a cldr-json territories.json.
func territoriesFixture(locale string, names map[string]string) string {
	encoded, _ := json.Marshal(map[string]any{"main": map[string]any{locale: map[string]any{
		"identity":           map[string]any{"language": locale},
		"localeDisplayNames": map[string]any{"territories": names},
	}}})
	return string(encoded)
}

var territoryFixtures = map[string]map[string]string{ //nolint:gochecknoglobals // test fixture.
	"en":      {"AR": "Argentina", "AQ": "Antarctica", "DE": "Germany", "419": "Latin America", "HK-alt-short": "Hong Kong"},
	"es-419":  {"AR": "Argentina", "AQ": "Antártida", "DE": "Alemania", "419": "Latinoamérica"},
	"pt":      {"AR": "Argentina", "AQ": "Antártida", "DE": "Alemanha"},
	"fr":      {"AR": "Argentine", "AQ": "Antarctique", "DE": "Allemagne"},
	"it":      {"AR": "Argentina", "AQ": "Antartide", "DE": "Germania"},
	"de":      {"AR": "Argentinien", "AQ": "Antarktis", "DE": "Deutschland"},
	"ru":      {"AR": "Аргентина", "AQ": "Антарктида", "DE": "Германия"},
	"zh-Hans": {"AR": "阿根廷", "AQ": "南极洲", "DE": "德国"},
	"ko":      {"AR": "아르헨티나", "AQ": "남극 대륙", "DE": "독일"},
	"ja":      {"AR": "アルゼンチン", "AQ": "南極", "DE": "ドイツ"},
}

// subdivisionsFixture renders a CLDR common/subdivisions XML.
func subdivisionsFixture(names map[string]string) string {
	var builder strings.Builder
	builder.WriteString(`<?xml version="1.0" encoding="UTF-8" ?><ldml><identity><version number="$Revision$"/></identity><localeDisplayNames><subdivisions>`)
	keys := make([]string, 0, len(names))
	for key := range names {
		keys = append(keys, key)
	}
	slices.Sort(keys)
	for _, key := range keys {
		builder.WriteString(`<subdivision type="` + key + `" draft="provisional">` + names[key] + `</subdivision>`)
	}
	builder.WriteString(`</subdivisions></localeDisplayNames></ldml>`)
	return builder.String()
}

var subdivisionFixtures = map[string]map[string]string{ //nolint:gochecknoglobals // test fixture.
	"en": {"arx": "Córdoba", "arc": "Buenos Aires", "debe": "Berlin"},
	"es": {"arx": "Provincia de Córdoba", "arc": "Ciudad Autónoma de Buenos Aires"},
	"pt": {"arx": "Córdova"},
	"fr": {}, "it": {}, "de": {}, "ru": {},
	"zh": {"arx": "科尔多瓦省"},
	"ko": {},
	"ja": {"arx": "コルドバ州"},
}

const validityFixture = `<?xml version="1.0" encoding="UTF-8" ?>
<supplementalData><idValidity>
	<id type='subdivision' idStatus='regular'>
		ara~c arx
		debe~f
	</id>
	<id type='subdivision' idStatus='deprecated'>arz</id>
</idValidity></supplementalData>`

const wikidataFixture = "# Wikidata (CC0): ISO 3166-2 code (P300) by GeoNames id (P1566).\n" +
	"# Query date: 2026-09-25\n" +
	"3860255\tAR-X\n" +
	"3860255\tAR-Z\n" + // deprecated in CLDR: ignored
	"2921044\tDE\n"

const overridesFixture = "# admin1 code\tISO 3166-2 code or -\treason\n" +
	"AR.07\tAR-C\tGeoNames id differs from the Wikidata item of the Autonomous City of Buenos Aires\n" +
	"DE.99\t-\tFixture unit without an ISO 3166-2 code\n"

func fixtureSources() sources {
	input := sources{
		countries:            strings.NewReader(countryInfoFixture),
		subdivisions:         strings.NewReader(admin1Fixture),
		timeZones:            strings.NewReader(timeZonesFixture),
		cities:               strings.NewReader(citiesFixture),
		alternateNames:       strings.NewReader(alternateNamesFixture),
		territoryNames:       map[string]io.Reader{},
		subdivisionNames:     map[string]io.Reader{},
		subdivisionValidity:  strings.NewReader(validityFixture),
		wikidataCodes:        strings.NewReader(wikidataFixture),
		subdivisionOverrides: strings.NewReader(overridesFixture),
	}
	for locale, names := range territoryFixtures {
		input.territoryNames[locale] = strings.NewReader(territoriesFixture(locale, names))
	}
	for locale, names := range subdivisionFixtures {
		input.subdivisionNames[locale] = strings.NewReader(subdivisionsFixture(names))
	}
	return input
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

func rowsByKey(rows [][]string) map[string][]string {
	keyed := map[string][]string{}
	for _, row := range rows {
		keyed[row[0]] = row
	}
	return keyed
}

func firstColumn(rows [][]string) []string {
	values := make([]string, 0, len(rows))
	for _, row := range rows {
		values = append(values, row[0])
	}
	return values
}

func TestBuildSnapshotListsTablesInLoadOrder(t *testing.T) {
	built := buildFixture(t)

	names := make([]string, 0, len(built.tables))
	for _, entry := range built.tables {
		names = append(names, entry.name)
	}
	want := []string{"places", "countries", "time_zones", "subdivisions", "cities", "place_names"}
	if !slices.Equal(names, want) {
		t.Errorf("tables = %v, want %v", names, want)
	}
}

func TestBuildSnapshotRegistersEveryPlaceWithItsKindAndOfficialName(t *testing.T) {
	places := rowsByKey(tableRows(t, buildFixture(t), "places"))

	want := map[string][]string{
		"3865483": {"3865483", "country", "Argentina"},
		"6697173": {"6697173", "country", "Antarctica"},
		"2921044": {"2921044", "country", "Germany"},
		// CLDR English names, UTF-8, replace GeoNames ASCII admin1 names.
		"3860255": {"3860255", "subdivision", "Córdoba"},
		"3433955": {"3433955", "subdivision", "Buenos Aires"},
		"2950157": {"2950157", "subdivision", "Berlin"},
		// No ISO code, so no CLDR name: the GeoNames name stays.
		"9999":    {"9999", "subdivision", "Nowhere Land"},
		"3832734": {"3832734", "city", "Villa General Belgrano"},
		"3860259": {"3860259", "city", "Córdoba"},
	}
	for key, row := range want {
		if !slices.Equal(places[key], row) {
			t.Errorf("place %s = %v, want %v", key, places[key], row)
		}
	}
	// XX.01 belongs to no known country and is dropped.
	if _, found := places["1"]; found {
		t.Error("subdivision of an unknown country was kept")
	}
}

func TestBuildSnapshotFiltersCitiesByRegionalPopulationThreshold(t *testing.T) {
	got := firstColumn(tableRows(t, buildFixture(t), "cities"))

	// Kept: Latin American towns above 500 (even without a known
	// subdivision), cities above 15000 elsewhere, and national capitals.
	// Dropped: exactly 500 in Latin America, 14000 elsewhere, and PPLX
	// sections of a populated place.
	want := []string{"1000002", "1000005", "2950159", "3435910", "3832734", "3860259"}
	if !slices.Equal(got, want) {
		t.Errorf("cities = %v, want %v", got, want)
	}
}

func TestBuildSnapshotResolvesCitySubdivisionAndTimeZone(t *testing.T) {
	cities := rowsByKey(tableRows(t, buildFixture(t), "cities"))

	want := []string{"3832734", "AR", "3860255", "Villa General Belgrano", "-31.97", "-64.56", "5888", "PPL", "America/Argentina/Cordoba"}
	if got := cities["3832734"]; !slices.Equal(got, want) {
		t.Errorf("Villa General Belgrano = %v, want %v", got, want)
	}
	if got := cities["1000002"][2]; got != nullValue {
		t.Errorf("unknown admin1 code subdivision = %q, want NULL", got)
	}
}

func TestBuildSnapshotResolvesSubdivisionISOCodes(t *testing.T) {
	subdivisions := rowsByKey(tableRows(t, buildFixture(t), "subdivisions"))

	want := map[string][]string{
		// Wikidata; its deprecated second code is ignored.
		"3860255": {"3860255", "AR", "AR-X", "05"},
		// Override.
		"3433955": {"3433955", "AR", "AR-C", "07"},
		// No Wikidata code: unique CLDR English name match.
		"2950157": {"2950157", "DE", "DE-BE", "16"},
		// Explicit "-" override: no ISO 3166-2 code.
		"9999": {"9999", "DE", nullValue, "99"},
	}
	for key, row := range want {
		if !slices.Equal(subdivisions[key], row) {
			t.Errorf("subdivision %s = %v, want %v", key, subdivisions[key], row)
		}
	}
}

func TestBuildSnapshotFailsForSubdivisionWithoutISOCode(t *testing.T) {
	fixture := fixtureSources()
	fixture.subdivisionOverrides = strings.NewReader("AR.07\tAR-C\tGeoNames id differs\n")

	_, err := buildSnapshot(fixture)
	if err == nil || !strings.Contains(err.Error(), "DE.99") {
		t.Fatalf("build without an override for DE.99: err = %v, want it named", err)
	}
}

func TestBuildSnapshotFailsForInvalidOrStaleOverride(t *testing.T) {
	for name, overrides := range map[string]string{
		"invalid code":   overridesFixture + "AR.05\tAR-Q\tnot a CLDR code\n",
		"stale override": overridesFixture + "AR.42\tAR-A\tno such GeoNames unit\n",
		"no reason":      "AR.07\tAR-C\t\nDE.99\t-\tnone\n",
		"duplicate code": "AR.07\tAR-X\tsame code as Cordoba\nDE.99\t-\tnone\n",
	} {
		t.Run(name, func(t *testing.T) {
			fixture := fixtureSources()
			fixture.subdivisionOverrides = strings.NewReader(overrides)
			if _, err := buildSnapshot(fixture); err == nil {
				t.Fatal("want error, got nil")
			}
		})
	}
}

func TestBuildSnapshotPicksCountryCapitalAndDefaultTimeZone(t *testing.T) {
	countries := rowsByKey(tableRows(t, buildFixture(t), "countries"))

	want := map[string][]string{
		"AR": {"AR", "3865483", "ARG", "32", "SA", "ARS", "3435910", "America/Argentina/Buenos_Aires"},
		// Two PPLC cities: the one named like countryInfo's capital wins.
		"DE": {"DE", "2921044", "DEU", "276", "EU", "EUR", "2950159", "Europe/Berlin"},
		"AQ": {"AQ", "6697173", "ATA", "10", "AN", nullValue, nullValue, nullValue},
	}
	for key, row := range want {
		if !slices.Equal(countries[key], row) {
			t.Errorf("country %s = %v, want %v", key, countries[key], row)
		}
	}
}

func TestBuildSnapshotLocalizesNames(t *testing.T) {
	names := map[string]string{}
	for _, row := range tableRows(t, buildFixture(t), "place_names") {
		names[row[0]+" "+row[1]] = row[2]
	}

	want := map[string]string{
		// Countries and subdivisions: CLDR (es-419 from cldr-json es-419,
		// pt-BR from pt, zh-Hans from zh-Hans and zh).
		"3865483 fr":      "Argentine",
		"3865483 de":      "Argentinien",
		"3865483 ru":      "Аргентина",
		"3865483 zh-Hans": "阿根廷",
		"3865483 ko":      "아르헨티나",
		"3865483 ja":      "アルゼンチン",
		"2921044 es-419":  "Alemania",
		"2921044 pt-BR":   "Alemanha",
		"2921044 fr":      "Allemagne",
		"2921044 it":      "Germania",
		"2921044 de":      "Deutschland",
		"2921044 ru":      "Германия",
		"2921044 zh-Hans": "德国",
		"2921044 ko":      "독일",
		"2921044 ja":      "ドイツ",
		"6697173 es-419":  "Antártida",
		"6697173 pt-BR":   "Antártida",
		"6697173 fr":      "Antarctique",
		"6697173 it":      "Antartide",
		"6697173 de":      "Antarktis",
		"6697173 ru":      "Антарктида",
		"6697173 zh-Hans": "南极洲",
		"6697173 ko":      "남극 대륙",
		"6697173 ja":      "南極",
		"3860255 es-419":  "Provincia de Córdoba",
		"3860255 pt-BR":   "Córdova",
		"3860255 zh-Hans": "科尔多瓦省",
		"3860255 ja":      "コルドバ州",
		"3433955 es-419":  "Ciudad Autónoma de Buenos Aires",
		// Cities: the best GeoNames alternate name. pt-BR prefers the
		// Brazilian variant; preferred beats plain; historic, colloquial
		// and ended names are never picked (it has none left); zh-Hans
		// takes zh-CN, never plain zh or zh-Hant.
		"3832734 ja":      "ヴィラ・ヘネラル・ベルグラーノ",
		"3860259 pt-BR":   "Córdoba (Argentina)",
		"3860259 en":      "Cordoba",
		"3860259 fr":      "Cordoue",
		"2950159 zh-Hans": "柏林市",
		"2950159 ru":      "Берлин",
	}
	for key, value := range want {
		if names[key] != value {
			t.Errorf("name %s = %q, want %q", key, names[key], value)
		}
	}
	// Equal to the own name (Argentina in es-419, Córdoba in en) or not
	// covered: not stored.
	for key, value := range names {
		if _, expected := want[key]; !expected {
			t.Errorf("unexpected name %s = %q", key, value)
		}
	}
}

func TestBuildSnapshotRejectsCityWithUnknownTimeZone(t *testing.T) {
	fixture := fixtureSources()
	fixture.cities = strings.NewReader(cityRow("1", "Lost", "Lost", "PPL", "AR", "05", "9000", "Mars/Olympus_Mons"))

	if _, err := buildSnapshot(fixture); err == nil {
		t.Fatal("build snapshot with an unknown city time zone: want error, got nil")
	}
}

func TestParseSubdivisionValidityExpandsRanges(t *testing.T) {
	codes, err := parseSubdivisionValidity(strings.NewReader(validityFixture))
	if err != nil {
		t.Fatalf("parse validity: %v", err)
	}
	got := make([]string, 0, len(codes))
	for code := range codes {
		got = append(got, code)
	}
	slices.Sort(got)
	if want := []string{"ara", "arb", "arc", "arx", "debe", "debf"}; !slices.Equal(got, want) {
		t.Errorf("regular codes = %v, want %v", got, want)
	}
}

func TestLocaleMappingsCoverTheDefaultSupportedLocales(t *testing.T) {
	for _, locale := range strings.Split(configuration.DefaultSupportedLocales, ",") {
		if !slices.ContainsFunc(localeLanguages, func(mapping localeMapping) bool { return mapping.locale == locale }) {
			t.Errorf("supported locale %q has no GeoNames language mapping", locale)
		}
		if !slices.ContainsFunc(cldrLocales, func(mapping cldrLocale) bool { return mapping.locale == locale }) {
			t.Errorf("supported locale %q has no CLDR mapping", locale)
		}
	}
}

func TestEscapeCopyFieldEscapesCopyTextSpecialCharacters(t *testing.T) {
	got := escapeCopyField("a\\b\tc\nd\re")
	if want := `a\\b\tc\nd\re`; got != want {
		t.Errorf("escapeCopyField = %q, want %q", got, want)
	}
}

var fixtureDescription = sourceDescription{ //nolint:gochecknoglobals // test fixture.
	dumpDate:          "2026-09-25",
	wikidataQueryDate: "2026-09-24",
	files:             []sourceFile{{name: "countryInfo.txt", origin: geonamesBaseURL + "countryInfo.txt", sha256: "abc"}},
}

func TestWriteSnapshotIsDeterministic(t *testing.T) {
	built := buildFixture(t)
	first, second := t.TempDir(), t.TempDir()

	if err := writeSnapshot(first, built, fixtureDescription); err != nil {
		t.Fatalf("write first snapshot: %v", err)
	}
	if err := writeSnapshot(second, buildFixture(t), fixtureDescription); err != nil {
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
			t.Errorf("%s differs between two builds of the same sources", entry.Name())
		}
	}
}

func TestWriteSnapshotWritesCopyTextAndManifest(t *testing.T) {
	directory := t.TempDir()
	if err := writeSnapshot(directory, buildFixture(t), fixtureDescription); err != nil {
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
	if first := strings.SplitN(string(content), "\n", 2)[0]; first != "AQ\t6697173\tATA\t10\tAN\t\\N\t\\N\t\\N" {
		t.Errorf("countries.tsv starts with %q", first)
	}

	manifestContent, err := os.ReadFile(filepath.Join(directory, "manifest.json"))
	if err != nil {
		t.Fatalf("read manifest: %v", err)
	}
	var parsed manifest
	if err := json.Unmarshal(manifestContent, &parsed); err != nil {
		t.Fatalf("parse manifest: %v", err)
	}
	if parsed.GeoNamesDumpDate != "2026-09-25" || parsed.CLDRVersion != cldrVersion ||
		parsed.WikidataQueryDate != "2026-09-24" || parsed.License != license || len(parsed.Sources) != 1 {
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
