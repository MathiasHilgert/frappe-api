// Command geosnapshot builds the geographic reference data snapshot that
// the geo seed migration loads (migrations/data/geo): countries, their
// first-level subdivisions, cities, IANA time zones and localized names,
// from the GeoNames dump (https://download.geonames.org/export/dump/,
// CC BY 4.0; see NOTICE).
//
// Usage:
//
//	go run ./cmd/geosnapshot -download -sources .geonames -dump-date 2026-09-25
//	go run ./cmd/geosnapshot -sources .geonames -dump-date 2026-09-25
//
// With -download it first fetches the source files into -sources.
// GeoNames only publishes its latest daily dump, so a past dump cannot be
// fetched again: the manifest pins the snapshot instead, recording the
// dump date and the SHA-256 of every source file, and the same source
// files always produce byte-identical snapshot files. Updating the data
// means running this command again and adding a new seed migration.
//
// Coverage: every country and every first-level subdivision; cities
// with a population above 500 in Latin America and the Caribbean, above
// 15000 elsewhere, plus every national capital (see coverage.go). Names:
// the best alternate name per platform locale (see names.go).
package main

import (
	"archive/zip"
	"context"
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"flag"
	"fmt"
	"io"
	"net/http"
	"os"
	"os/signal"
	"path/filepath"
	"regexp"
	"time"
)

// downloadBaseURL is the official GeoNames dump location.
const downloadBaseURL = "https://download.geonames.org/export/dump/"

// Source file names, as published by GeoNames.
const (
	countriesFile      = "countryInfo.txt"
	subdivisionsFile   = "admin1CodesASCII.txt"
	timeZonesFile      = "timeZones.txt"
	citiesArchive      = "cities500.zip"
	citiesFile         = "cities500.txt"
	alternateArchive   = "alternateNamesV2.zip"
	alternateNamesFile = "alternateNamesV2.txt"
)

// sourceFileNames lists every downloaded file, in manifest order.
var sourceFileNames = []string{countriesFile, subdivisionsFile, timeZonesFile, citiesArchive, alternateArchive} //nolint:gochecknoglobals // constant list.

var dumpDatePattern = regexp.MustCompile(`^\d{4}-\d{2}-\d{2}$`)

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
	sourceDirectory := flags.String("sources", ".geonames", "directory holding (or receiving, with -download) the GeoNames dump files")
	outputDirectory := flags.String("output", "migrations/data/geo", "directory the snapshot is written to")
	dumpDate := flags.String("dump-date", "", "date (YYYY-MM-DD) the GeoNames dump files were published; recorded in the manifest")
	download := flags.Bool("download", false, "download the GeoNames dump files into -sources first")
	if err := flags.Parse(arguments); err != nil {
		return err
	}
	if !dumpDatePattern.MatchString(*dumpDate) {
		return errors.New("-dump-date is required, formatted YYYY-MM-DD")
	}

	if *download {
		if err := downloadSources(ctx, *sourceDirectory); err != nil {
			return err
		}
	}

	description, err := describeSources(*sourceDirectory, *dumpDate)
	if err != nil {
		return err
	}
	built, err := buildFromDirectory(*sourceDirectory)
	if err != nil {
		return err
	}
	if err := writeSnapshot(*outputDirectory, built, description); err != nil {
		return err
	}
	for _, entry := range built.tables {
		fmt.Printf("%-18s %8d rows\n", entry.name, len(entry.rows))
	}
	return nil
}

func downloadSources(ctx context.Context, directory string) error {
	if err := os.MkdirAll(directory, 0o750); err != nil {
		return fmt.Errorf("create sources directory: %w", err)
	}
	client := &http.Client{Timeout: 30 * time.Minute}
	for _, name := range sourceFileNames {
		if err := downloadFile(ctx, client, downloadBaseURL+name, filepath.Join(directory, name)); err != nil {
			return err
		}
	}
	return nil
}

func downloadFile(ctx context.Context, client *http.Client, url, destination string) error {
	request, err := http.NewRequestWithContext(ctx, http.MethodGet, url, nil)
	if err != nil {
		return fmt.Errorf("build request for %s: %w", url, err)
	}
	response, err := client.Do(request)
	if err != nil {
		return fmt.Errorf("download %s: %w", url, err)
	}
	defer func() { _ = response.Body.Close() }()
	if response.StatusCode != http.StatusOK {
		return fmt.Errorf("download %s: status %s", url, response.Status)
	}
	file, err := os.Create(destination) //nolint:gosec // destination is built from the -sources flag and a constant file name.
	if err != nil {
		return fmt.Errorf("create %s: %w", destination, err)
	}
	if _, err := io.Copy(file, response.Body); err != nil {
		_ = file.Close()
		return fmt.Errorf("write %s: %w", destination, err)
	}
	return file.Close()
}

// describeSources hashes every source file for the manifest.
func describeSources(directory, dumpDate string) (sourceDescription, error) {
	description := sourceDescription{dumpDate: dumpDate}
	for _, name := range sourceFileNames {
		digest, err := hashFile(filepath.Join(directory, name))
		if err != nil {
			return sourceDescription{}, err
		}
		description.files = append(description.files, sourceFile{name: name, sha256: digest})
	}
	return description, nil
}

func hashFile(path string) (string, error) {
	file, err := os.Open(path) //nolint:gosec // path is built from the -sources flag and a constant file name.
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

// buildFromDirectory opens the source files (reading the two large ones
// straight out of their zip archives) and builds the snapshot.
func buildFromDirectory(directory string) (snapshot, error) {
	var closers []io.Closer
	defer func() {
		for _, closer := range closers {
			_ = closer.Close()
		}
	}()
	open := func(name string) (io.Reader, error) {
		file, err := os.Open(filepath.Join(directory, name)) //nolint:gosec // path is built from the -sources flag and a constant file name.
		if err != nil {
			return nil, fmt.Errorf("open %s: %w", name, err)
		}
		closers = append(closers, file)
		return file, nil
	}
	openArchived := func(archive, member string) (io.Reader, error) {
		reader, err := zip.OpenReader(filepath.Join(directory, archive))
		if err != nil {
			return nil, fmt.Errorf("open %s: %w", archive, err)
		}
		closers = append(closers, reader)
		file, err := reader.Open(member)
		if err != nil {
			return nil, fmt.Errorf("open %s in %s: %w", member, archive, err)
		}
		closers = append(closers, file)
		return file, nil
	}

	var input sources
	var err error
	if input.countries, err = open(countriesFile); err != nil {
		return snapshot{}, err
	}
	if input.subdivisions, err = open(subdivisionsFile); err != nil {
		return snapshot{}, err
	}
	if input.timeZones, err = open(timeZonesFile); err != nil {
		return snapshot{}, err
	}
	if input.cities, err = openArchived(citiesArchive, citiesFile); err != nil {
		return snapshot{}, err
	}
	if input.alternateNames, err = openArchived(alternateArchive, alternateNamesFile); err != nil {
		return snapshot{}, err
	}
	return buildSnapshot(input)
}
