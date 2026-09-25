package databasetest

import (
	"crypto/sha256"
	"encoding/hex"
	"errors"

	"github.com/jackc/pgx/v5/pgconn"
)

// This file carries no "//go:build integration" tag on purpose: it holds
// only pure helpers, which lets helpers_test.go unit-test them without
// Docker. It imports nothing test-only.

// containerNamePrefix is combined with testcontainers.SessionID (see
// containerNameForSession) into the name container.go passes to
// testcontainers.WithReuseByName. The session ID is derived from the
// parent "go test" process (its PID and creation time), so every package
// test binary of one "go test" invocation shares a single container,
// while a second, concurrent invocation on the same Docker host gets its
// own container and can never attach to one that Ryuk reaps when the
// first invocation ends.
const containerNamePrefix = "frappe-api-test-database"

// sessionSuffixLength is how many hexadecimal characters of the hashed
// session ID end up in the container name: enough to make a collision
// between concurrent invocations on one Docker host negligible.
const sessionSuffixLength = 16

// SQLSTATE codes Postgres reports when a CREATE ROLE races another one
// for the same name: duplicate_object when the role already exists, and
// unique_violation when both inserts into pg_authid collide.
const (
	duplicateObjectCode = "42710"
	uniqueViolationCode = "23505"
)

// containerNameForSession returns the per-invocation container name for
// sessionID. The session ID is hashed so the name stays a valid, bounded
// Docker name whatever the session ID looks like, including empty.
func containerNameForSession(sessionID string) string {
	digest := sha256.Sum256([]byte(sessionID))
	return containerNamePrefix + "-" + hex.EncodeToString(digest[:])[:sessionSuffixLength]
}

// isAlreadyExistsError reports whether err means a role (or another
// object) already exists because a concurrent package test binary
// created it first, which bootstrapRoles treats as success.
func isAlreadyExistsError(err error) bool {
	var postgresError *pgconn.PgError
	if !errors.As(err, &postgresError) {
		return false
	}
	return postgresError.Code == duplicateObjectCode || postgresError.Code == uniqueViolationCode
}
