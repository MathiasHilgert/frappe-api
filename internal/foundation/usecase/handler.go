// Package usecase is the shape every module's application use case takes
// and the observability wrapped around it at wiring time.
//
// A use case is one type with one method, Handle, taking its input (a
// query or a command) and returning its result:
//
//	type GetCountryHandler struct{ countries application.CountryReader }
//	func (handler *GetCountryHandler) Handle(ctx context.Context, query GetCountry) (domain.Country, error)
//
// The module root wraps each handler with NewObserved, so every call gets a
// span, RED metrics and a structured log named after the handler type
// ("geo.query.get_country") without a line of telemetry in the use case.
// Adapters depend on the QueryHandler or CommandHandler interface, never on
// the concrete handler, and test with fakes. Only business metrics (for
// example how many results a search returned) stay hand-written, in the
// application layer.
//
// The name "usecase" is the layer's own vocabulary: the handlers are the
// application's use cases, and the package holds nothing else.
package usecase

import "context"

// Handler is one use case: it handles an Input and returns a Result.
type Handler[Input, Result any] interface {
	Handle(ctx context.Context, input Input) (Result, error)
}

// QueryHandler is a use case that reads and changes nothing.
type QueryHandler[Query, Result any] = Handler[Query, Result]

// CommandHandler is a use case that changes state.
type CommandHandler[Command, Result any] = Handler[Command, Result]
