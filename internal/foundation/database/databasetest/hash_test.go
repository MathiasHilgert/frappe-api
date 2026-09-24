package databasetest_test

import (
	"testing"
	"testing/fstest"

	"github.com/peterldowns/pgtestdb/migrators/goosemigrator"
)

// This file carries no "//go:build integration" tag on purpose, unlike
// the rest of this package: it exercises goosemigrator.Hash's stability
// and change-sensitivity, the property databasetest.New and
// databasetest.NewOwner rely on to key a template database by migration
// content without ever starting a container or connecting to Postgres,
// so it runs as a plain unit test.

// migrationOne and migrationTwo are minimal, syntactically valid goose
// migration files; their content, not their validity against a real
// database, is all Hash reads.
const (
	migrationOne = `-- +goose Up
SELECT 1;
-- +goose Down
SELECT 1;
`
	migrationTwo = `-- +goose Up
SELECT 2;
-- +goose Down
SELECT 2;
`
)

func TestMigrationHashIsStableForTheSameMigrationsContent(t *testing.T) {
	firstFilesystem := fstest.MapFS{"00001_first.sql": &fstest.MapFile{Data: []byte(migrationOne)}}
	secondFilesystem := fstest.MapFS{"00001_first.sql": &fstest.MapFile{Data: []byte(migrationOne)}}

	firstHash, err := goosemigrator.New(".", goosemigrator.WithFS(firstFilesystem)).Hash()
	if err != nil {
		t.Fatalf("Hash (first) returned unexpected error: %v", err)
	}
	secondHash, err := goosemigrator.New(".", goosemigrator.WithFS(secondFilesystem)).Hash()
	if err != nil {
		t.Fatalf("Hash (second) returned unexpected error: %v", err)
	}

	if firstHash != secondHash {
		t.Fatalf("Hash = %q and %q for identical migration content, want equal", firstHash, secondHash)
	}
	if firstHash == "" {
		t.Fatal("Hash returned an empty string for non-empty migration content")
	}
}

func TestMigrationHashChangesWhenMigrationContentChanges(t *testing.T) {
	baseFilesystem := fstest.MapFS{"00001_first.sql": &fstest.MapFile{Data: []byte(migrationOne)}}
	changedFilesystem := fstest.MapFS{"00001_first.sql": &fstest.MapFile{Data: []byte(migrationTwo)}}

	baseHash, err := goosemigrator.New(".", goosemigrator.WithFS(baseFilesystem)).Hash()
	if err != nil {
		t.Fatalf("Hash (base) returned unexpected error: %v", err)
	}
	changedHash, err := goosemigrator.New(".", goosemigrator.WithFS(changedFilesystem)).Hash()
	if err != nil {
		t.Fatalf("Hash (changed) returned unexpected error: %v", err)
	}

	if baseHash == changedHash {
		t.Fatalf("Hash = %q for both the original and the changed migration content, want different values", baseHash)
	}
}

func TestMigrationHashChangesWhenAMigrationFileIsAdded(t *testing.T) {
	baseFilesystem := fstest.MapFS{"00001_first.sql": &fstest.MapFile{Data: []byte(migrationOne)}}
	extendedFilesystem := fstest.MapFS{
		"00001_first.sql":  &fstest.MapFile{Data: []byte(migrationOne)},
		"00002_second.sql": &fstest.MapFile{Data: []byte(migrationTwo)},
	}

	baseHash, err := goosemigrator.New(".", goosemigrator.WithFS(baseFilesystem)).Hash()
	if err != nil {
		t.Fatalf("Hash (base) returned unexpected error: %v", err)
	}
	extendedHash, err := goosemigrator.New(".", goosemigrator.WithFS(extendedFilesystem)).Hash()
	if err != nil {
		t.Fatalf("Hash (extended) returned unexpected error: %v", err)
	}

	if baseHash == extendedHash {
		t.Fatalf("Hash = %q for both the original and the extended migration set, want different values", baseHash)
	}
}
