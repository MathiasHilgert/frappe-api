package migrations_test

import (
	"bufio"
	"compress/gzip"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"io/fs"
	"testing"

	"github.com/MathiasHilgert/frappe-api/migrations"
)

type geoManifest struct {
	License           string `json:"license"`
	GeoNamesDumpDate  string `json:"geonamesDumpDate"`
	CLDRVersion       string `json:"cldrVersion"`
	WikidataQueryDate string `json:"wikidataQueryDate"`
	Sources           []struct {
		File   string `json:"file"`
		Origin string `json:"origin"`
		SHA256 string `json:"sha256"`
	} `json:"sources"`
	Tables []struct {
		File   string `json:"file"`
		SHA256 string `json:"sha256"`
		Rows   int    `json:"rows"`
	} `json:"tables"`
}

// TestGeoSnapshotMatchesItsManifest proves the embedded snapshot is
// exactly the one its provenance record describes: every table file's
// SHA-256 and row count, and a complete source record.
func TestGeoSnapshotMatchesItsManifest(t *testing.T) {
	content, err := fs.ReadFile(migrations.Data, "data/geo/manifest.json")
	if err != nil {
		t.Fatalf("read manifest: %v", err)
	}
	var manifest geoManifest
	if err = json.Unmarshal(content, &manifest); err != nil {
		t.Fatalf("parse manifest: %v", err)
	}
	if manifest.License == "" || manifest.GeoNamesDumpDate == "" || manifest.CLDRVersion == "" || manifest.WikidataQueryDate == "" {
		t.Errorf("manifest misses provenance: %+v", manifest)
	}
	for _, source := range manifest.Sources {
		if source.File == "" || source.Origin == "" || len(source.SHA256) != 64 {
			t.Errorf("incomplete source record %+v", source)
		}
	}

	tables, err := fs.Glob(migrations.Data, "data/geo/*.tsv.gz")
	if err != nil {
		t.Fatalf("list snapshot: %v", err)
	}
	if len(tables) != len(manifest.Tables) {
		t.Fatalf("snapshot has %d table files, manifest lists %d", len(tables), len(manifest.Tables))
	}
	for _, table := range manifest.Tables {
		compressed, err := fs.ReadFile(migrations.Data, "data/geo/"+table.File)
		if err != nil {
			t.Fatalf("read %s: %v", table.File, err)
		}
		digest := sha256.Sum256(compressed)
		if got := hex.EncodeToString(digest[:]); got != table.SHA256 {
			t.Errorf("%s sha256 = %s, manifest says %s", table.File, got, table.SHA256)
		}
		if rows := countRows(t, table.File); rows != table.Rows {
			t.Errorf("%s has %d rows, manifest says %d", table.File, rows, table.Rows)
		}
	}
}

func countRows(t *testing.T, file string) int {
	t.Helper()
	opened, err := migrations.Data.Open("data/geo/" + file)
	if err != nil {
		t.Fatalf("open %s: %v", file, err)
	}
	defer func() { _ = opened.Close() }()
	reader, err := gzip.NewReader(opened)
	if err != nil {
		t.Fatalf("decompress %s: %v", file, err)
	}
	scanner := bufio.NewScanner(reader)
	scanner.Buffer(make([]byte, 0, 64*1024), 1<<20)
	rows := 0
	for scanner.Scan() {
		rows++
	}
	if err := scanner.Err(); err != nil {
		t.Fatalf("read %s: %v", file, err)
	}
	return rows
}
