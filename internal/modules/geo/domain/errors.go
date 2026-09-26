package domain

import "errors"

// ErrNotFound reports a place or time zone that does not exist.
var ErrNotFound = errors.New("geo: not found")
