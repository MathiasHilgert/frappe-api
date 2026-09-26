package main

import (
	"bufio"
	"fmt"
	"io"
	"strconv"
	"strings"
)

// The parsers below read the GeoNames dump formats documented in
// https://download.geonames.org/export/dump/readme.txt. Every file is UTF-8,
// tab separated, one record per line.

// maximumLineLength bounds one dump line; alternate names are at most 400
// characters and geoname rows carry an alternatenames column of at most
// 10000, so 1 MiB leaves ample room.
const maximumLineLength = 1 << 20

type country struct {
	code         string
	alpha3Code   string
	name         string
	capital      string
	continent    string
	currencyCode string
	numericCode  int
	geonamesID   int64
}

type subdivision struct {
	countryCode string
	code        string
	name        string
	asciiName   string
	geonamesID  int64
}

type timeZone struct {
	identifier    string
	countryCode   string
	januaryOffset string
	julyOffset    string
	rawOffset     string
}

type city struct {
	name        string
	asciiName   string
	latitude    string
	longitude   string
	featureCode string
	countryCode string
	admin1Code  string
	timeZone    string
	geonamesID  int64
	population  int64
}

type alternateName struct {
	language   string
	name       string
	endedAt    string
	identifier int64
	geonamesID int64
	preferred  bool
	short      bool
	colloquial bool
	historic   bool
}

// eachLine calls handle with the tab separated fields of every line of
// reader that is neither empty nor a "#" comment, and names the source and
// line number in any error.
func eachLine(reader io.Reader, source string, handle func(fields []string) error) error {
	scanner := bufio.NewScanner(reader)
	scanner.Buffer(make([]byte, 0, 64*1024), maximumLineLength)
	lineNumber := 0
	for scanner.Scan() {
		lineNumber++
		line := scanner.Text()
		if line == "" || strings.HasPrefix(line, "#") {
			continue
		}
		if err := handle(strings.Split(line, "\t")); err != nil {
			return fmt.Errorf("%s line %d: %w", source, lineNumber, err)
		}
	}
	if err := scanner.Err(); err != nil {
		return fmt.Errorf("read %s: %w", source, err)
	}
	return nil
}

func requireFields(fields []string, minimum int) error {
	if len(fields) < minimum {
		return fmt.Errorf("got %d columns, want at least %d", len(fields), minimum)
	}
	return nil
}

// parseCountries reads countryInfo.txt.
func parseCountries(reader io.Reader) ([]country, error) {
	var countries []country
	err := eachLine(reader, "countryInfo.txt", func(fields []string) error {
		if err := requireFields(fields, 17); err != nil {
			return err
		}
		numericCode, err := strconv.Atoi(fields[2])
		if err != nil {
			return fmt.Errorf("numeric code %q: %w", fields[2], err)
		}
		geonamesID, err := strconv.ParseInt(fields[16], 10, 64)
		if err != nil {
			return fmt.Errorf("geonameid %q: %w", fields[16], err)
		}
		countries = append(countries, country{
			code:         fields[0],
			alpha3Code:   fields[1],
			numericCode:  numericCode,
			geonamesID:   geonamesID,
			name:         fields[4],
			capital:      fields[5],
			continent:    fields[8],
			currencyCode: fields[10],
		})
		return nil
	})
	return countries, err
}

// parseSubdivisions reads admin1CodesASCII.txt, whose first column is
// "<country code>.<admin1 code>".
func parseSubdivisions(reader io.Reader) ([]subdivision, error) {
	var subdivisions []subdivision
	err := eachLine(reader, "admin1CodesASCII.txt", func(fields []string) error {
		if err := requireFields(fields, 4); err != nil {
			return err
		}
		countryCode, code, found := strings.Cut(fields[0], ".")
		if !found {
			return fmt.Errorf("admin1 code %q has no country prefix", fields[0])
		}
		geonamesID, err := strconv.ParseInt(fields[3], 10, 64)
		if err != nil {
			return fmt.Errorf("geonameid %q: %w", fields[3], err)
		}
		subdivisions = append(subdivisions, subdivision{
			geonamesID:  geonamesID,
			countryCode: countryCode,
			code:        code,
			name:        fields[1],
			asciiName:   fields[2],
		})
		return nil
	})
	return subdivisions, err
}

// parseTimeZones reads timeZones.txt, skipping its header line.
func parseTimeZones(reader io.Reader) ([]timeZone, error) {
	var timeZones []timeZone
	err := eachLine(reader, "timeZones.txt", func(fields []string) error {
		if fields[0] == "CountryCode" {
			return nil
		}
		if err := requireFields(fields, 5); err != nil {
			return err
		}
		timeZones = append(timeZones, timeZone{
			countryCode:   fields[0],
			identifier:    fields[1],
			januaryOffset: fields[2],
			julyOffset:    fields[3],
			rawOffset:     fields[4],
		})
		return nil
	})
	return timeZones, err
}

// parseCities reads a geoname table file (cities500.txt) and keeps the
// rows include accepts.
func parseCities(reader io.Reader, include func(city) bool) ([]city, error) {
	var cities []city
	err := eachLine(reader, "cities500.txt", func(fields []string) error {
		if err := requireFields(fields, 18); err != nil {
			return err
		}
		geonamesID, err := strconv.ParseInt(fields[0], 10, 64)
		if err != nil {
			return fmt.Errorf("geonameid %q: %w", fields[0], err)
		}
		population, err := strconv.ParseInt(fields[14], 10, 64)
		if err != nil {
			return fmt.Errorf("population %q: %w", fields[14], err)
		}
		candidate := city{
			geonamesID:  geonamesID,
			name:        fields[1],
			asciiName:   fields[2],
			latitude:    fields[4],
			longitude:   fields[5],
			featureCode: fields[7],
			countryCode: fields[8],
			admin1Code:  fields[10],
			population:  population,
			timeZone:    fields[17],
		}
		if include(candidate) {
			cities = append(cities, candidate)
		}
		return nil
	})
	return cities, err
}

// scanAlternateNames streams alternateNamesV2.txt and calls handle for
// every row whose geonameid wanted accepts. The file holds millions of
// rows, so it is never loaded whole.
func scanAlternateNames(reader io.Reader, wanted func(int64) bool, handle func(alternateName)) error {
	return eachLine(reader, "alternateNamesV2.txt", func(fields []string) error {
		if err := requireFields(fields, 4); err != nil {
			return err
		}
		geonamesID, err := strconv.ParseInt(fields[1], 10, 64)
		if err != nil {
			return fmt.Errorf("geonameid %q: %w", fields[1], err)
		}
		if !wanted(geonamesID) {
			return nil
		}
		identifier, err := strconv.ParseInt(fields[0], 10, 64)
		if err != nil {
			return fmt.Errorf("alternateNameId %q: %w", fields[0], err)
		}
		flag := func(index int) bool { return index < len(fields) && fields[index] == "1" }
		endedAt := ""
		if len(fields) > 9 {
			endedAt = fields[9]
		}
		handle(alternateName{
			identifier: identifier,
			geonamesID: geonamesID,
			language:   fields[2],
			name:       fields[3],
			preferred:  flag(4),
			short:      flag(5),
			colloquial: flag(6),
			historic:   flag(7),
			endedAt:    endedAt,
		})
		return nil
	})
}
