// Command geosnapshot builds the geographic reference data snapshot that
// the geo seed migration loads (migrations/data/geo): places (countries,
// first-level subdivisions, cities), IANA time zones and localized names.
//
// Sources (see NOTICE):
//
//   - GeoNames dump (https://download.geonames.org/export/dump/, CC BY 4.0):
//     countries, subdivisions, cities, coordinates, population, time zones
//     and city names.
//   - Unicode CLDR (pinned to cldrVersion, Unicode License v3): country and
//     subdivision names in every platform locale, and the valid ISO 3166-2
//     subdivision codes.
//   - Wikidata (CC0): ISO 3166-2 codes (P300) of GeoNames admin1 units
//     (P1566), committed as data/wikidata_subdivision_codes.tsv, plus the
//     reviewed data/subdivision_overrides.tsv for the units Wikidata does
//     not match. The build fails when a subdivision has no code.
//
// Usage:
//
//	go run ./cmd/geosnapshot -download -sources .geonames -dump-date 2026-09-25
//	go run ./cmd/geosnapshot -sources .geonames -dump-date 2026-09-25
//	go run ./cmd/geosnapshot -refresh-wikidata -sources .geonames -dump-date 2026-09-25
//
// -download fetches the GeoNames and CLDR files into -sources;
// -refresh-wikidata queries Wikidata again and rewrites the committed
// Wikidata snapshot. GeoNames only publishes its latest daily dump and the
// Wikidata endpoint is live, so neither can be fetched again as it was: the
// manifest pins the snapshot instead (GeoNames dump date, CLDR version,
// Wikidata query date and the SHA-256 of every source file), and the same
// source files always produce byte-identical snapshot files. Updating the
// data means running this command again and adding a new seed migration.
package main

import (
	"archive/zip"
	"bufio"
	"context"
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"flag"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"os"
	"os/signal"
	"path/filepath"
	"regexp"
	"slices"
	"strings"
	"time"
)

const (
	geonamesBaseURL        = "https://download.geonames.org/export/dump/"
	cldrTerritoriesBaseURL = "https://cdn.jsdelivr.net/npm/cldr-localenames-full@" + cldrVersion + "/main/"
	cldrRepositoryBaseURL  = "https://raw.githubusercontent.com/unicode-org/cldr/release-48-2/common/"
	wikidataEndpoint       = "https://query.wikidata.org/sparql"
	// wikidataQuery lists every item with both an ISO 3166-2 code (P300)
	// and a GeoNames id (P1566).
	wikidataQuery = "SELECT ?geonames ?iso WHERE { ?item wdt:P300 ?iso ; wdt:P1566 ?geonames . }"
)

// GeoNames file names.
const (
	countriesFile      = "countryInfo.txt"
	subdivisionsFile   = "admin1CodesASCII.txt"
	timeZonesFile      = "timeZones.txt"
	citiesArchive      = "cities500.zip"
	citiesFile         = "cities500.txt"
	alternateArchive   = "alternateNamesV2.zip"
	alternateNamesFile = "alternateNamesV2.txt"
)

// Committed inputs, under -inputs.
const (
	wikidataFile  = "wikidata_subdivision_codes.tsv"
	overridesFile = "subdivision_overrides.tsv"
)

// validityFile is the downloaded CLDR subdivision validity file.
const validityFile = "cldr-subdivision-validity.xml"

var datePattern = regexp.MustCompile(`^\d{4}-\d{2}-\d{2}$`)

// downloadedFile is one file fetched into -sources.
type downloadedFile struct {
	name   string
	origin string
}

func territoryFile(locale string) string   { return "cldr-territories-" + locale + ".json" }
func subdivisionFile(locale string) string { return "cldr-subdivisions-" + locale + ".xml" }

// downloadedFiles lists every file -download fetches, in manifest order.
func downloadedFiles() []downloadedFile {
	files := make([]downloadedFile, 0, 16)
	for _, name := range []string{countriesFile, subdivisionsFile, timeZonesFile, citiesArchive, alternateArchive} {
		files = append(files, downloadedFile{name: name, origin: geonamesBaseURL + name})
	}
	var territories, subdivisions []string
	for _, mapping := range cldrLocales {
		if !slices.Contains(territories, mapping.territory) {
			territories = append(territories, mapping.territory)
		}
		if !slices.Contains(subdivisions, mapping.subdivision) {
			subdivisions = append(subdivisions, mapping.subdivision)
		}
	}
	for _, locale := range territories {
		files = append(files, downloadedFile{name: territoryFile(locale), origin: cldrTerritoriesBaseURL + locale + "/territories.json"})
	}
	for _, locale := range subdivisions {
		files = append(files, downloadedFile{name: subdivisionFile(locale), origin: cldrRepositoryBaseURL + "subdivisions/" + locale + ".xml"})
	}
	files = append(files, downloadedFile{name: validityFile, origin: cldrRepositoryBaseURL + "validity/subdivision.xml"})
	return files
}

func main() {
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt)
	err := run(ctx, os.Args[1:])
	stop()
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}

func run(ctx context.Context, arguments []string) error {
	flags := flag.NewFlagSet("geosnapshot", flag.ContinueOnError)
	sourceDirectory := flags.String("sources", ".geonames", "directory holding (or receiving, with -download) the GeoNames and CLDR files")
	inputDirectory := flags.String("inputs", "cmd/geosnapshot/data", "directory of the committed Wikidata snapshot and override file")
	outputDirectory := flags.String("output", "migrations/data/geo", "directory the snapshot is written to")
	dumpDate := flags.String("dump-date", "", "date (YYYY-MM-DD) the GeoNames dump files were published; recorded in the manifest")
	download := flags.Bool("download", false, "download the GeoNames and CLDR files into -sources first")
	refreshWikidata := flags.Bool("refresh-wikidata", false, "query Wikidata again and rewrite the committed Wikidata snapshot")
	if err := flags.Parse(arguments); err != nil {
		return err
	}
	if !datePattern.MatchString(*dumpDate) {
		return errors.New("-dump-date is required, formatted YYYY-MM-DD")
	}

	if err := fetch(ctx, *download, *refreshWikidata, *sourceDirectory, *inputDirectory); err != nil {
		return err
	}

	description, err := describeSources(*sourceDirectory, *inputDirectory, *dumpDate)
	if err != nil {
		return err
	}
	built, err := buildFromDirectories(*sourceDirectory, *inputDirectory)
	if err != nil {
		return err
	}
	if err := writeSnapshot(*outputDirectory, built, description); err != nil {
		return err
	}
	for _, entry := range built.tables {
		fmt.Printf("%-14s %8d rows\n", entry.name, len(entry.rows))
	}
	return nil
}

// fetch downloads the sources and refreshes the Wikidata snapshot, as
// asked.
func fetch(ctx context.Context, download, refreshWikidata bool, sourceDirectory, inputDirectory string) error {
	client := &http.Client{Timeout: 30 * time.Minute}
	if download {
		if err := downloadSources(ctx, client, sourceDirectory); err != nil {
			return err
		}
	}
	if refreshWikidata {
		return downloadWikidata(ctx, client, filepath.Join(inputDirectory, wikidataFile))
	}
	return nil
}

func downloadSources(ctx context.Context, client *http.Client, directory string) error {
	if err := os.MkdirAll(directory, 0o750); err != nil {
		return fmt.Errorf("create sources directory: %w", err)
	}
	for _, file := range downloadedFiles() {
		if err := downloadFile(ctx, client, file.origin, filepath.Join(directory, file.name), nil); err != nil {
			return err
		}
	}
	return nil
}

func downloadFile(ctx context.Context, client *http.Client, address, destination string, header http.Header) error {
	request, err := http.NewRequestWithContext(ctx, http.MethodGet, address, nil)
	if err != nil {
		return fmt.Errorf("build request for %s: %w", address, err)
	}
	for name, values := range header {
		request.Header[name] = values
	}
	response, err := client.Do(request)
	if err != nil {
		return fmt.Errorf("download %s: %w", address, err)
	}
	defer func() { _ = response.Body.Close() }()
	if response.StatusCode != http.StatusOK {
		return fmt.Errorf("download %s: status %s", address, response.Status)
	}
	file, err := os.Create(destination) //nolint:gosec // destination is built from a flag and a constant file name.
	if err != nil {
		return fmt.Errorf("create %s: %w", destination, err)
	}
	if _, err := io.Copy(file, response.Body); err != nil {
		_ = file.Close()
		return fmt.Errorf("write %s: %w", destination, err)
	}
	return file.Close()
}

// downloadWikidata runs wikidataQuery and writes its result, sorted and
// deduplicated, with a header recording the query and its date.
func downloadWikidata(ctx context.Context, client *http.Client, destination string) error {
	temporary := destination + ".download"
	address := wikidataEndpoint + "?" + url.Values{"query": {wikidataQuery}}.Encode()
	header := http.Header{
		"Accept":     {"text/tab-separated-values"},
		"User-Agent": {"frappe-api-geosnapshot/1.0 (https://github.com/MathiasHilgert/frappe-api)"},
	}
	if err := downloadFile(ctx, client, address, temporary, header); err != nil {
		return err
	}
	defer func() { _ = os.Remove(temporary) }()

	content, err := os.ReadFile(temporary) //nolint:gosec // path is built from a flag and a constant file name.
	if err != nil {
		return fmt.Errorf("read Wikidata result: %w", err)
	}
	var lines []string
	for index, line := range strings.Split(strings.TrimSpace(string(content)), "\n") {
		if index == 0 {
			continue
		}
		fields := strings.Split(line, "\t")
		if len(fields) != 2 {
			return fmt.Errorf("unexpected Wikidata row %q", line)
		}
		lines = append(lines, strings.Trim(fields[0], `"`)+"\t"+strings.Trim(fields[1], `"`))
	}
	slices.Sort(lines)
	lines = slices.Compact(lines)
	document := "# Wikidata (CC0): ISO 3166-2 code (P300) by GeoNames id (P1566).\n" +
		"# Query date: " + time.Now().UTC().Format(time.DateOnly) + "\n" +
		"# Query: " + wikidataQuery + "\n" +
		strings.Join(lines, "\n") + "\n"
	return os.WriteFile(destination, []byte(document), 0o600) //nolint:gosec // destination is built from a flag and a constant file name.
}

// wikidataQueryDate reads the query date from the committed snapshot.
func wikidataQueryDate(path string) (string, error) {
	file, err := os.Open(path) //nolint:gosec // path is built from a flag and a constant file name.
	if err != nil {
		return "", fmt.Errorf("open %s: %w", path, err)
	}
	defer func() { _ = file.Close() }()
	scanner := bufio.NewScanner(file)
	for scanner.Scan() {
		if date, found := strings.CutPrefix(scanner.Text(), "# Query date: "); found && datePattern.MatchString(date) {
			return date, nil
		}
	}
	return "", fmt.Errorf("%s has no \"# Query date: YYYY-MM-DD\" line", path)
}

// describeSources hashes every source file for the manifest.
func describeSources(sourceDirectory, inputDirectory, dumpDate string) (sourceDescription, error) {
	queryDate, err := wikidataQueryDate(filepath.Join(inputDirectory, wikidataFile))
	if err != nil {
		return sourceDescription{}, err
	}
	description := sourceDescription{dumpDate: dumpDate, wikidataQueryDate: queryDate}
	for _, file := range downloadedFiles() {
		digest, err := hashFile(filepath.Join(sourceDirectory, file.name))
		if err != nil {
			return sourceDescription{}, err
		}
		description.files = append(description.files, sourceFile{name: file.name, origin: file.origin, sha256: digest})
	}
	for _, committed := range []sourceFile{
		{name: wikidataFile, origin: wikidataEndpoint},
		{name: overridesFile, origin: "cmd/geosnapshot/data"},
	} {
		digest, err := hashFile(filepath.Join(inputDirectory, committed.name))
		if err != nil {
			return sourceDescription{}, err
		}
		committed.sha256 = digest
		description.files = append(description.files, committed)
	}
	return description, nil
}

func hashFile(path string) (string, error) {
	file, err := os.Open(path) //nolint:gosec // path is built from a flag and a constant file name.
	if err != nil {
		return "", fmt.Errorf("open %s: %w", path, err)
	}
	defer func() { _ = file.Close() }()
	hash := sha256.New()
	if _, err := io.Copy(hash, file); err != nil {
		return "", fmt.Errorf("hash %s: %w", path, err)
	}
	return hex.EncodeToString(hash.Sum(nil)), nil
}

// buildFromDirectories opens every source file (reading the two large
// GeoNames files straight out of their zip archives) and builds the
// snapshot.
func buildFromDirectories(sourceDirectory, inputDirectory string) (snapshot, error) {
	var closers []io.Closer
	defer func() {
		for _, closer := range closers {
			_ = closer.Close()
		}
	}()
	var openError error
	open := func(directory, name string) io.Reader {
		file, err := os.Open(filepath.Join(directory, name)) //nolint:gosec // path is built from a flag and a constant file name.
		if err != nil {
			openError = errors.Join(openError, fmt.Errorf("open %s: %w", name, err))
			return strings.NewReader("")
		}
		closers = append(closers, file)
		return file
	}
	openArchived := func(archive, member string) io.Reader {
		reader, err := zip.OpenReader(filepath.Join(sourceDirectory, archive))
		if err != nil {
			openError = errors.Join(openError, fmt.Errorf("open %s: %w", archive, err))
			return strings.NewReader("")
		}
		closers = append(closers, reader)
		file, err := reader.Open(member)
		if err != nil {
			openError = errors.Join(openError, fmt.Errorf("open %s in %s: %w", member, archive, err))
			return strings.NewReader("")
		}
		closers = append(closers, file)
		return file
	}

	input := sources{
		countries:            open(sourceDirectory, countriesFile),
		subdivisions:         open(sourceDirectory, subdivisionsFile),
		timeZones:            open(sourceDirectory, timeZonesFile),
		cities:               openArchived(citiesArchive, citiesFile),
		alternateNames:       openArchived(alternateArchive, alternateNamesFile),
		territoryNames:       map[string]io.Reader{},
		subdivisionNames:     map[string]io.Reader{},
		subdivisionValidity:  open(sourceDirectory, validityFile),
		wikidataCodes:        open(inputDirectory, wikidataFile),
		subdivisionOverrides: open(inputDirectory, overridesFile),
	}
	for _, mapping := range cldrLocales {
		if _, found := input.territoryNames[mapping.territory]; !found {
			input.territoryNames[mapping.territory] = open(sourceDirectory, territoryFile(mapping.territory))
		}
		if _, found := input.subdivisionNames[mapping.subdivision]; !found {
			input.subdivisionNames[mapping.subdivision] = open(sourceDirectory, subdivisionFile(mapping.subdivision))
		}
	}
	if openError != nil {
		return snapshot{}, openError
	}
	return buildSnapshot(input)
}
