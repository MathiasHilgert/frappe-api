package main

import (
	"fmt"
	"io"
	"slices"
	"strconv"
	"strings"
	"unicode"

	"golang.org/x/text/unicode/norm"
)

// noISOCode marks, in the override file, a GeoNames admin1 unit that has
// no current ISO 3166-2 code; it is stored with a NULL iso_code.
const noISOCode = "-"

// subdivisionOverride is one line of the override file.
type subdivisionOverride struct {
	isoCode string
	reason  string
}

// parseWikidataCodes reads the committed Wikidata snapshot: GeoNames id,
// tab, ISO 3166-2 code (P1566 and P300 of the same item). One GeoNames id
// may carry several codes.
func parseWikidataCodes(reader io.Reader) (map[int64][]string, error) {
	codes := map[int64][]string{}
	err := eachLine(reader, "wikidata_subdivision_codes.tsv", func(fields []string) error {
		if err := requireFields(fields, 2); err != nil {
			return err
		}
		geonamesID, err := strconv.ParseInt(fields[0], 10, 64)
		if err != nil {
			return fmt.Errorf("GeoNames id %q: %w", fields[0], err)
		}
		codes[geonamesID] = append(codes[geonamesID], fields[1])
		return nil
	})
	return codes, err
}

// parseSubdivisionOverrides reads the committed override file: GeoNames
// admin1 code ("AR.07"), tab, ISO 3166-2 code or "-", tab, reason.
func parseSubdivisionOverrides(reader io.Reader) (map[string]subdivisionOverride, error) {
	overrides := map[string]subdivisionOverride{}
	err := eachLine(reader, "subdivision_overrides.tsv", func(fields []string) error {
		if err := requireFields(fields, 3); err != nil {
			return err
		}
		if strings.TrimSpace(fields[2]) == "" {
			return fmt.Errorf("override for %s has no reason", fields[0])
		}
		if _, duplicate := overrides[fields[0]]; duplicate {
			return fmt.Errorf("duplicate override for %s", fields[0])
		}
		overrides[fields[0]] = subdivisionOverride{isoCode: fields[1], reason: fields[2]}
		return nil
	})
	return overrides, err
}

// isoCodeSources is everything resolveISOCodes matches against.
type isoCodeSources struct {
	wikidata     map[int64][]string
	overrides    map[string]subdivisionOverride
	valid        map[string]bool   // CLDR regular subdivision codes ("arx")
	englishNames map[string]string // CLDR English names by CLDR code
}

// resolveISOCodes assigns every subdivision its ISO 3166-2 code, returned
// by GeoNames id ("" for an explicit "-" override). In order:
//
//  1. an override for its admin1 code;
//  2. the one Wikidata code of its GeoNames id that belongs to its country
//     and is a regular CLDR subdivision code;
//  3. the one regular CLDR code of its country whose English name equals
//     its GeoNames name, ignoring case and accents.
//
// It fails, listing every problem, when a subdivision matches nothing, a
// code is assigned twice, an override names an invalid code, or an
// override is not used (a stale entry).
func resolveISOCodes(subdivisions []subdivision, input isoCodeSources) (map[int64]string, error) {
	resolved := map[int64]string{}
	owner := map[string]string{}
	usedOverrides := map[string]bool{}
	var problems []string

	for _, entry := range subdivisions {
		admin1 := entry.countryCode + "." + entry.code
		code, problem := resolveISOCode(entry, admin1, input, usedOverrides)
		if problem != "" {
			problems = append(problems, problem)
			continue
		}
		if code != "" {
			if previous, taken := owner[code]; taken {
				problems = append(problems, fmt.Sprintf("%s and %s both resolve to %s; add an override", previous, admin1, code))
				continue
			}
			owner[code] = admin1
		}
		resolved[entry.geonamesID] = code
	}
	for admin1 := range input.overrides {
		if !usedOverrides[admin1] {
			problems = append(problems, fmt.Sprintf("override for %s matches no GeoNames subdivision; remove it", admin1))
		}
	}
	if len(problems) > 0 {
		slices.Sort(problems)
		return nil, fmt.Errorf("%d subdivisions without a valid ISO 3166-2 code (fix cmd/geosnapshot/data/subdivision_overrides.tsv):\n%s",
			len(problems), strings.Join(problems, "\n"))
	}
	return resolved, nil
}

func resolveISOCode(entry subdivision, admin1 string, input isoCodeSources, usedOverrides map[string]bool) (code, problem string) {
	if override, found := input.overrides[admin1]; found {
		usedOverrides[admin1] = true
		return overrideISOCode(entry, admin1, override, input.valid)
	}
	candidates := wikidataISOCodes(entry, input)
	if len(candidates) == 1 {
		return candidates[0], ""
	}
	if len(candidates) > 1 {
		return "", fmt.Sprintf("%s (%s, GeoNames %d) has several Wikidata codes %v; add an override", admin1, entry.name, entry.geonamesID, candidates)
	}
	if byName := englishNameISOCodes(entry, input); len(byName) == 1 {
		return byName[0], ""
	}
	return "", fmt.Sprintf("%s (%s, GeoNames %d) matches no ISO 3166-2 code; add an override", admin1, entry.name, entry.geonamesID)
}

func overrideISOCode(entry subdivision, admin1 string, override subdivisionOverride, valid map[string]bool) (code, problem string) {
	if override.isoCode == noISOCode {
		return "", ""
	}
	if !strings.HasPrefix(override.isoCode, entry.countryCode+"-") || !valid[cldrSubdivisionCode(override.isoCode)] {
		return "", fmt.Sprintf("override %s -> %s is not a regular CLDR subdivision code of %s", admin1, override.isoCode, entry.countryCode)
	}
	return override.isoCode, ""
}

// wikidataISOCodes returns the distinct Wikidata codes of the subdivision
// that belong to its country and are regular CLDR codes, sorted.
func wikidataISOCodes(entry subdivision, input isoCodeSources) []string {
	var candidates []string
	for _, candidate := range input.wikidata[entry.geonamesID] {
		if strings.HasPrefix(candidate, entry.countryCode+"-") && input.valid[cldrSubdivisionCode(candidate)] {
			candidates = append(candidates, candidate)
		}
	}
	slices.Sort(candidates)
	return slices.Compact(candidates)
}

// englishNameISOCodes returns the regular CLDR codes of the subdivision's
// country whose English name equals its GeoNames name, ignoring case and
// accents, as ISO 3166-2 codes.
func englishNameISOCodes(entry subdivision, input isoCodeSources) []string {
	var matches []string
	prefix := strings.ToLower(entry.countryCode)
	for cldrCode, name := range input.englishNames {
		if strings.HasPrefix(cldrCode, prefix) && input.valid[cldrCode] && foldName(name) == foldName(entry.name) {
			matches = append(matches, strings.ToUpper(cldrCode[:2])+"-"+strings.ToUpper(cldrCode[2:]))
		}
	}
	return matches
}

// foldName lower-cases name and strips its accents, for comparing names.
func foldName(name string) string {
	var builder strings.Builder
	for _, character := range norm.NFD.String(strings.ToLower(name)) {
		if !unicode.Is(unicode.Mn, character) {
			builder.WriteRune(character)
		}
	}
	return builder.String()
}
