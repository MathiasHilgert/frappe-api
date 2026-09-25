package databasetest

import (
	"errors"
	"fmt"
	"strings"
	"testing"

	"github.com/jackc/pgx/v5/pgconn"
)

// This file carries no "//go:build integration" tag on purpose: the
// helpers it covers are pure functions that need neither Docker nor
// Postgres, so they run as plain unit tests.

func TestContainerNameForSessionDiffersPerSession(t *testing.T) {
	first := containerNameForSession("aaaaaaaaaaaaaaaaaaaaaaaa")
	second := containerNameForSession("bbbbbbbbbbbbbbbbbbbbbbbb")

	if first == second {
		t.Fatalf("containerNameForSession returned %q for two different sessions, want different names", first)
	}
	if !strings.HasPrefix(first, containerNamePrefix+"-") {
		t.Fatalf("containerNameForSession = %q, want prefix %q", first, containerNamePrefix+"-")
	}
}

func TestContainerNameForSessionIsStableWithinOneSession(t *testing.T) {
	sessionID := "session"
	first := containerNameForSession(sessionID)
	second := containerNameForSession(sessionID)
	if first != second {
		t.Fatal("containerNameForSession is not deterministic for the same session")
	}
}

func TestContainerNameForSessionHandlesAnEmptySession(t *testing.T) {
	if name := containerNameForSession(""); name == containerNamePrefix+"-" {
		t.Fatalf("containerNameForSession(\"\") = %q, want a non-empty suffix", name)
	}
}

func TestIsAlreadyExistsErrorAcceptsDuplicateObjectAndUniqueViolation(t *testing.T) {
	for _, code := range []string{"42710", "23505"} {
		wrapped := fmt.Errorf("wrapped: %w", &pgconn.PgError{Code: code})
		if !isAlreadyExistsError(wrapped) {
			t.Errorf("isAlreadyExistsError(SQLSTATE %s) = false, want true", code)
		}
	}
}

func TestIsAlreadyExistsErrorRejectsOtherErrors(t *testing.T) {
	if isAlreadyExistsError(nil) {
		t.Error("isAlreadyExistsError(nil) = true, want false")
	}
	if isAlreadyExistsError(errors.New("connection refused")) {
		t.Error("isAlreadyExistsError(plain error) = true, want false")
	}
	if isAlreadyExistsError(&pgconn.PgError{Code: "42501"}) {
		t.Error("isAlreadyExistsError(insufficient_privilege) = true, want false")
	}
}
