// Package query holds the geo module's read use cases, one handler per
// file. Each handler implements usecase.QueryHandler: its query type (for
// example GetCountry) is the input and the handler (GetCountryHandler)
// reads through the application ports. The module root wraps every
// handler with usecase.NewObserved, so none of them carries telemetry
// code besides business metrics.
package query
