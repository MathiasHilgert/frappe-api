// Package application holds the ports the geo use cases read through, one
// file per port; the use cases themselves are in application/query. Every
// read takes the request's locale: names come back in it, falling back to
// the place's own name.
package application
