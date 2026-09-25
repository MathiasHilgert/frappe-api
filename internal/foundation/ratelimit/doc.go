// Package ratelimit implements a distributed request rate limiter backed
// by Valkey, using the Generic Cell Rate Algorithm (GCRA).
//
// # Why GCRA and not valkeylimiter
//
// valkey-go ships github.com/valkey-io/valkey-go/valkeylimiter, a fixed
// window counter. It was not used because:
//
//   - a fixed window allows up to twice the limit in a burst straddling
//     a window boundary;
//   - its Lua script takes the current time from each caller, so replicas
//     with skewed clocks disagree about when a window started;
//   - it builds and owns its own client instead of accepting the shared,
//     lifecycle-managed one.
//
// GCRA stores a single value per key (the theoretical arrival time), has
// no boundary bursts, and yields everything the IETF RateLimit headers
// need (remaining quota, reset time, retry time). The whole
// read-decide-write step runs in one Lua script executed with EVALSHA
// (falling back to EVAL on NOSCRIPT, handled by valkey-go's LuaScript),
// so it is atomic on the server: concurrent requests from any number of
// replicas can never jointly exceed the limit. The script reads the
// clock with the server's own TIME command, so replica clock skew does
// not matter. Valkey replicates scripts by effects, so calling TIME in a
// script that writes is safe.
//
// # Semantics
//
// Settings.Requests requests are allowed per Settings.Window, as a burst
// of up to Requests; after that, one request is earned back every
// Window / Requests. A key idle for a whole Window has its full quota
// again, and expires from Valkey on its own.
//
// Like every foundation package it is a leaf: it imports no other
// first-party package, and the composition root adapts Limiter to
// internal/foundation/httpserver.RateLimiter.
package ratelimit
