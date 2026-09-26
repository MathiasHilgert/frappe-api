package main

import (
	"encoding/json"
	"encoding/xml"
	"errors"
	"fmt"
	"io"
	"regexp"
	"strings"
)

// cldrVersion pins the Unicode CLDR release (Unicode License v3) that
// supplies country and subdivision names and the valid subdivision codes.
const cldrVersion = "48.2.0"

// cldrLocales maps every platform locale to the CLDR locale files it reads:
// territory names from cldr-json (resolved, so es-419 exists on its own)
// and subdivision names from the CLDR XML (which only has language-level
// files; pt is Brazilian Portuguese and zh is Simplified Chinese in CLDR).
var cldrLocales = []cldrLocale{ //nolint:gochecknoglobals // constant lookup table.
	{locale: "es-419", territory: "es-419", subdivision: "es"},
	{locale: "en", territory: "en", subdivision: "en"},
	{locale: "pt-BR", territory: "pt", subdivision: "pt"},
	{locale: "fr", territory: "fr", subdivision: "fr"},
	{locale: "it", territory: "it", subdivision: "it"},
	{locale: "de", territory: "de", subdivision: "de"},
	{locale: "ru", territory: "ru", subdivision: "ru"},
	{locale: "zh-Hans", territory: "zh-Hans", subdivision: "zh"},
	{locale: "ko", territory: "ko", subdivision: "ko"},
	{locale: "ja", territory: "ja", subdivision: "ja"},
}

type cldrLocale struct {
	locale      string
	territory   string
	subdivision string
}

var territoryCodePattern = regexp.MustCompile(`^[A-Z]{2}$`)

// parseTerritoryNames reads a cldr-localenames-full territories.json and
// returns the name of every ISO 3166-1 alpha-2 territory (alternative
// forms such as "HK-alt-short" and region codes such as "419" are skipped).
func parseTerritoryNames(reader io.Reader) (map[string]string, error) {
	var document struct {
		Main map[string]struct {
			LocaleDisplayNames struct {
				Territories map[string]string `json:"territories"`
			} `json:"localeDisplayNames"`
		} `json:"main"`
	}
	if err := json.NewDecoder(reader).Decode(&document); err != nil {
		return nil, fmt.Errorf("decode CLDR territories: %w", err)
	}
	names := map[string]string{}
	for _, locale := range document.Main {
		for code, name := range locale.LocaleDisplayNames.Territories {
			if territoryCodePattern.MatchString(code) && name != "" {
				names[code] = name
			}
		}
	}
	if len(names) == 0 {
		return nil, errors.New("CLDR territories file has no territory names")
	}
	return names, nil
}

// parseSubdivisionNames reads a CLDR common/subdivisions/<locale>.xml and
// returns names by CLDR subdivision code (lower case ISO 3166-2 without
// the hyphen, "arx" for AR-X). Provisional entries are kept: outside
// English almost every subdivision name is still provisional in CLDR.
func parseSubdivisionNames(reader io.Reader) (map[string]string, error) {
	var document struct {
		Subdivisions []struct {
			Type string `xml:"type,attr"`
			Name string `xml:",chardata"`
		} `xml:"localeDisplayNames>subdivisions>subdivision"`
	}
	if err := xml.NewDecoder(reader).Decode(&document); err != nil {
		return nil, fmt.Errorf("decode CLDR subdivisions: %w", err)
	}
	names := map[string]string{}
	for _, entry := range document.Subdivisions {
		if name := strings.TrimSpace(entry.Name); name != "" {
			names[entry.Type] = name
		}
	}
	return names, nil
}

// parseSubdivisionValidity reads CLDR common/validity/subdivision.xml and
// returns the regular (current) subdivision codes. The file compresses
// runs with "~": "ad02~8" is ad02 through ad08, "afbal~m" afbal through
// afbam (the suffix replaces the last characters and counts up).
func parseSubdivisionValidity(reader io.Reader) (map[string]bool, error) {
	var document struct {
		Identifiers []struct {
			Type   string `xml:"type,attr"`
			Status string `xml:"idStatus,attr"`
			Codes  string `xml:",chardata"`
		} `xml:"idValidity>id"`
	}
	if err := xml.NewDecoder(reader).Decode(&document); err != nil {
		return nil, fmt.Errorf("decode CLDR subdivision validity: %w", err)
	}
	codes := map[string]bool{}
	for _, entry := range document.Identifiers {
		if entry.Type != "subdivision" || entry.Status != "regular" {
			continue
		}
		for _, token := range strings.Fields(entry.Codes) {
			expanded, err := expandValidityRange(token)
			if err != nil {
				return nil, err
			}
			for _, code := range expanded {
				codes[code] = true
			}
		}
	}
	if len(codes) == 0 {
		return nil, errors.New("CLDR validity file has no regular subdivision codes")
	}
	return codes, nil
}

const validityAlphabet = "0123456789abcdefghijklmnopqrstuvwxyz"

func expandValidityRange(token string) ([]string, error) {
	start, end, isRange := strings.Cut(token, "~")
	if !isRange {
		return []string{token}, nil
	}
	if end == "" || len(end) > len(start) {
		return nil, fmt.Errorf("invalid CLDR validity range %q", token)
	}
	prefix := start[:len(start)-len(end)]
	current := []byte(start[len(start)-len(end):])
	var codes []string
	for range 100000 {
		codes = append(codes, prefix+string(current))
		if string(current) == end {
			return codes, nil
		}
		if err := incrementValidityCode(current); err != nil {
			return nil, fmt.Errorf("CLDR validity range %q: %w", token, err)
		}
	}
	return nil, fmt.Errorf("CLDR validity range %q is too long", token)
}

// incrementValidityCode counts code up by one in validityAlphabet.
func incrementValidityCode(code []byte) error {
	for index := len(code) - 1; index >= 0; index-- {
		position := strings.IndexByte(validityAlphabet, code[index])
		if position < 0 {
			return fmt.Errorf("invalid character %q", code[index])
		}
		if position+1 < len(validityAlphabet) {
			code[index] = validityAlphabet[position+1]
			return nil
		}
		code[index] = validityAlphabet[0]
	}
	return errors.New("never reaches its end")
}

// cldrSubdivisionCode turns an ISO 3166-2 code ("AR-X") into its CLDR form
// ("arx").
func cldrSubdivisionCode(isoCode string) string {
	return strings.ToLower(strings.ReplaceAll(isoCode, "-", ""))
}
