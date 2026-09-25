// Package valkey is the foundation layer for the application's Valkey
// connection: Up connects and pings, Down closes, and Check pings, so the
// composition root can register the client as an application.Dependency
// with a background health check, exactly like the database pool.
//
// It uses github.com/valkey-io/valkey-go, the official Valkey Go client
// (auto-pipelining, RESP3, cluster aware). Client-side caching is
// disabled because no consumer needs it yet.
//
// # Telemetry
//
// The client is built with github.com/valkey-io/valkey-go/valkeyotel,
// which uses the global tracer and meter providers (OpenTelemetry API
// only). Every command produces a span named after the command (for
// example EVALSHA) and the metrics below.
//
//	Metric                           Kind             Attributes
//	valkey_command_duration_seconds  Float64Histogram none by default; unit s
//	valkey_command_errors            Int64Counter     none by default
//	valkey_dial_attempt              Int64Counter     none by default
//	valkey_dial_success              Int64Counter     none by default
//	valkey_dial_conns                Int64UpDownCounter none by default
//	valkey_dial_latency              Float64Histogram none by default; unit s
//	valkey_do_cache_hits/miss        Int64Counter     client-side caching only (disabled)
//
// Cardinality rule: command arguments are never recorded. db.statement
// is left disabled (valkeyotel.WithDBStatement is not used), because
// arguments include rate limit keys derived from client addresses; no
// key, client address or value may ever become a span or metric
// attribute.
//
// Like every foundation package it is a leaf: it imports no other
// first-party package. Consumers (for example internal/foundation/ratelimit)
// receive the valkey-go client from the composition root.
package valkey
