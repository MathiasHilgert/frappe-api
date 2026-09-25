// Package valkey is the foundation layer for the application's Valkey
// connection: Up connects and pings, Down closes, and Check pings, so the
// composition root can register the client as an application.Dependency
// with a background health check, exactly like the database pool.
//
// It uses github.com/valkey-io/valkey-go, the official Valkey Go client
// (auto-pipelining, RESP3, cluster aware). Client-side caching is
// disabled because no consumer needs it yet.
//
// Like every foundation package it is a leaf: it imports no other
// first-party package. Consumers (for example internal/foundation/ratelimit)
// receive the valkey-go client from the composition root.
package valkey
