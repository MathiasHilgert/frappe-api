package main

import (
	"io"
	"strings"
	"testing"
)

func TestStripDisambiguationRemovesTrailingParentheses(t *testing.T) {
	for input, want := range map[string]string{
		"Córdova (província da Argentina)": "Córdova",
		"コルドバ（アルゼンチン）":                     "コルドバ",
		"Berlin (Stadt) (Deutschland)":     "Berlin",
		"Saint-Denis":                      "Saint-Denis",
		"(Nur Klammer)":                    "",
		"Ciudad (Vieja) Norte":             "Ciudad (Vieja) Norte",
		"Baucau (municipalité":             "Baucau",
		"Реал дель Валье (Эль Параисо) [массив]": "Реал дель Валье",
		"ココス(キーリング)諸島":                           "ココス(キーリング)諸島",
	} {
		if got := stripDisambiguation(input); got != want {
			t.Errorf("stripDisambiguation(%q) = %q, want %q", input, got, want)
		}
	}
}

func TestParseSubdivisionNamesPrefersApprovedEntries(t *testing.T) {
	document := `<ldml><localeDisplayNames><subdivisions>` +
		`<subdivision type="arx" draft="provisional">Provisional</subdivision>` +
		`<subdivision type="arx">Approved</subdivision>` +
		`<subdivision type="arx" draft="provisional">Later Provisional</subdivision>` +
		`</subdivisions></localeDisplayNames></ldml>`
	names, err := parseSubdivisionNames(strings.NewReader(document))
	if err != nil {
		t.Fatalf("parse: %v", err)
	}
	if names["arx"] != "Approved" {
		t.Errorf("arx = %q, want Approved", names["arx"])
	}
}

func TestBuildSnapshotPlaceNamesCarryNoDisambiguation(t *testing.T) {
	fixture := fixtureSources()
	fixture.alternateNames = io.MultiReader(strings.NewReader(alternateNamesFixture),
		strings.NewReader(alternateRow("90", "3832734", "de", "Villa General Belgrano (Córdoba)", "1", "", "", "", "")))
	built, err := buildSnapshot(fixture)
	if err != nil {
		t.Fatalf("build: %v", err)
	}
	for _, row := range tableRows(t, built, "place_names") {
		if strings.ContainsAny(row[2], "(（") {
			t.Errorf("place name %v keeps a parenthesized disambiguation", row)
		}
		if row[0] == "3832734" && row[1] == "de" {
			t.Errorf("name equal to the own name after stripping was stored: %v", row)
		}
	}
}
