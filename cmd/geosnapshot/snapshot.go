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
	"strings"
)

// nullValue marks a NULL field in a table row; it is written as COPY's \N.
// Source text never contains a NUL byte, so it cannot clash with data.
const nullValue = "\x00"

// license is recorded in the manifest; see NOTICE.
const license = "GeoNames (https://www.geonames.org), CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/); " +
	"Unicode CLDR (https://cldr.unicode.org), Unicode License v3 (https://www.unicode.org/license.txt); " +
	"Wikidata (https://www.wikidata.org), CC0 1.0"

// escapeCopyField escapes value for PostgreSQL's COPY text format.
func escapeCopyField(value string) string {
	if value == nullValue {
		return `\N`
	}
	return strings.NewReplacer(`\`, `\\`, "\t", `\t`, "\n", `\n`, "\r", `\r`).Replace(value)
}

// sourceFile is one source file a snapshot was built from.
type sourceFile struct {
	name   string
	origin string
	sha256 string
}

// sourceDescription records where a snapshot came from.
type sourceDescription struct {
	dumpDate          string
	wikidataQueryDate string
	files             []sourceFile
}

type manifestSource struct {
	File   string `json:"file"`
	Origin string `json:"origin"`
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
	License           string           `json:"license"`
	GeoNamesDumpDate  string           `json:"geonamesDumpDate"`
	CLDRVersion       string           `json:"cldrVersion"`
	WikidataQueryDate string           `json:"wikidataQueryDate"`
	Sources           []manifestSource `json:"sources"`
	Tables            []manifestTable  `json:"tables"`
}

// writeSnapshot writes every table as "<name>.tsv.gz" (COPY text format,
// gzip without a timestamp, so identical rows give identical bytes) and
// manifest.json into directory.
func writeSnapshot(directory string, built snapshot, source sourceDescription) error {
	if err := os.MkdirAll(directory, 0o750); err != nil {
		return fmt.Errorf("create snapshot directory: %w", err)
	}
	written := manifest{
		License:           license,
		GeoNamesDumpDate:  source.dumpDate,
		CLDRVersion:       cldrVersion,
		WikidataQueryDate: source.wikidataQueryDate,
	}
	for _, file := range source.files {
		written.Sources = append(written.Sources, manifestSource{File: file.name, Origin: file.origin, SHA256: file.sha256})
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
