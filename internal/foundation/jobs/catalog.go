package jobs

import (
	"context"
	"errors"
	"fmt"
	"regexp"
	"sync"
	"time"
)

// segmentPattern matches a module name, an action and a queue name:
// lowercase snake_case starting with a letter.
var segmentPattern = regexp.MustCompile(`^[a-z][a-z0-9_]*$`)

// namePattern matches a full job name, <module>.<action>. Names are built
// from validated segments, so it only guards the invariant.
var namePattern = regexp.MustCompile(`^[a-z][a-z0-9_]*\.[a-z][a-z0-9_]*$`)

// Tenancy captures the tenant of the enqueuing context and restores it for
// the handler, so a handler opening a Row Level Security scoped transaction
// runs as the tenant that enqueued the job.
type Tenancy struct {
	// Resolve returns the tenant of ctx and whether one is known. Nil
	// captures no tenant.
	Resolve func(ctx context.Context) (string, bool)
	// Bind returns a copy of ctx carrying tenant. Nil leaves ctx as is;
	// the tenant is still available as Job.Tenant.
	Bind func(ctx context.Context, tenant string) context.Context
}

// Catalog holds every job definition, handler and schedule of the process.
// Modules declare their jobs on the default catalog through For; tests may
// build an isolated one with NewCatalog. It is safe for concurrent use.
type Catalog struct {
	enqueuer      Enqueuer
	tenancy       Tenancy
	definitions   map[string]*entry
	schedules     map[string]struct{}
	order         []string
	scheduleOrder []Schedule
	mutex         sync.RWMutex
}

// entry is what the catalog knows about one definition.
type entry struct {
	handler     Handler
	name        string
	queue       string
	timeout     time.Duration
	maxAttempts int
}

// NewCatalog returns an empty Catalog.
func NewCatalog() *Catalog {
	return &Catalog{definitions: map[string]*entry{}, schedules: map[string]struct{}{}}
}

// defaultCatalog is the process-wide catalog behind For.
var defaultCatalog = NewCatalog()

// Default returns the process-wide catalog modules declare their jobs on.
// The composition root installs the enqueuer on it and hands it to the
// backend adapter.
func Default() *Catalog {
	return defaultCatalog
}

// For returns the module scope of the default catalog; see Catalog.For.
func For(module string) *Module {
	return defaultCatalog.For(module)
}

// Module scopes job definitions to one module: every job it defines is
// named <module>.<action>.
type Module struct {
	catalog *Catalog
	name    string
}

// For returns the scope of module, which must be a lowercase snake_case
// identifier; it panics otherwise, a programming error caught at startup.
func (catalog *Catalog) For(module string) *Module {
	if !segmentPattern.MatchString(module) {
		panic(fmt.Sprintf("jobs: invalid module name %q: must match %s", module, segmentPattern.String()))
	}
	return &Module{catalog: catalog, name: module}
}

// Name returns the module name.
func (module *Module) Name() string {
	return module.name
}

// Use installs the enqueuer every Definition.Enqueue of this catalog goes
// to, unless the context carries one (ContextWithEnqueuer).
func (catalog *Catalog) Use(enqueuer Enqueuer) {
	catalog.mutex.Lock()
	defer catalog.mutex.Unlock()
	catalog.enqueuer = enqueuer
}

// UseTenancy installs how tenants are captured on enqueue and restored on
// execution.
func (catalog *Catalog) UseTenancy(tenancy Tenancy) {
	catalog.mutex.Lock()
	defer catalog.mutex.Unlock()
	catalog.tenancy = tenancy
}

func (catalog *Catalog) currentEnqueuer() Enqueuer {
	catalog.mutex.RLock()
	defer catalog.mutex.RUnlock()
	return catalog.enqueuer
}

func (catalog *Catalog) currentTenancy() Tenancy {
	catalog.mutex.RLock()
	defer catalog.mutex.RUnlock()
	return catalog.tenancy
}

// define registers a new definition, panicking on a duplicate name.
func (catalog *Catalog) define(definition *entry) {
	if !namePattern.MatchString(definition.name) {
		panic(fmt.Sprintf("jobs: invalid job name %q: must match %s", definition.name, namePattern.String()))
	}
	catalog.mutex.Lock()
	defer catalog.mutex.Unlock()
	if _, exists := catalog.definitions[definition.name]; exists {
		panic(fmt.Sprintf("jobs: duplicate job name %q", definition.name))
	}
	catalog.definitions[definition.name] = definition
	catalog.order = append(catalog.order, definition.name)
}

// handle attaches handler to the definition named name, panicking when one
// is already attached.
func (catalog *Catalog) handle(name string, handler Handler) {
	catalog.mutex.Lock()
	defer catalog.mutex.Unlock()
	definition := catalog.definitions[name]
	if definition.handler != nil {
		panic(fmt.Sprintf("jobs: job %q already has a handler", name))
	}
	definition.handler = handler
}

// schedule adds a periodic schedule, panicking on a duplicate identifier.
func (catalog *Catalog) schedule(schedule Schedule) {
	catalog.mutex.Lock()
	defer catalog.mutex.Unlock()
	if _, exists := catalog.schedules[schedule.Identifier]; exists {
		panic(fmt.Sprintf("jobs: duplicate schedule %q", schedule.Identifier))
	}
	catalog.schedules[schedule.Identifier] = struct{}{}
	catalog.scheduleOrder = append(catalog.scheduleOrder, schedule)
}

// Validate reports every definition without a handler. Jobs are private to
// the module that defines them, so a job nobody handles would never run.
// The backend adapter calls it at startup.
func (catalog *Catalog) Validate() error {
	catalog.mutex.RLock()
	defer catalog.mutex.RUnlock()
	var problems []error
	for _, name := range catalog.order {
		if catalog.definitions[name].handler == nil {
			problems = append(problems, fmt.Errorf("jobs: job %q has no handler; register one with jobs.Handle", name))
		}
	}
	return errors.Join(problems...)
}

// Registrations returns every handled definition in definition order.
func (catalog *Catalog) Registrations() []Registration {
	catalog.mutex.RLock()
	defer catalog.mutex.RUnlock()
	registrations := make([]Registration, 0, len(catalog.order))
	for _, name := range catalog.order {
		definition := catalog.definitions[name]
		if definition.handler == nil {
			continue
		}
		registrations = append(registrations, Registration{
			Handler:     definition.handler,
			Name:        definition.name,
			Queue:       definition.queue,
			Timeout:     definition.timeout,
			MaxAttempts: definition.maxAttempts,
		})
	}
	return registrations
}

// Schedules returns every periodic schedule in registration order.
func (catalog *Catalog) Schedules() []Schedule {
	catalog.mutex.RLock()
	defer catalog.mutex.RUnlock()
	return append([]Schedule(nil), catalog.scheduleOrder...)
}

// Queues returns every queue used by a definition, in first-use order.
func (catalog *Catalog) Queues() []string {
	catalog.mutex.RLock()
	defer catalog.mutex.RUnlock()
	seen := map[string]struct{}{}
	var queues []string
	for _, name := range catalog.order {
		queue := catalog.definitions[name].queue
		if _, found := seen[queue]; !found {
			seen[queue] = struct{}{}
			queues = append(queues, queue)
		}
	}
	return queues
}
