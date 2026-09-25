//go:build integration

package databasetest

import (
	"context"
	"fmt"
	"sync"

	"github.com/testcontainers/testcontainers-go"
	"github.com/testcontainers/testcontainers-go/modules/postgres"
)

// containerImage is pinned to the same version compose.yaml uses for the
// local database service, so integration tests run against the same
// Postgres major and minor version as local development and production.
const containerImage = "postgres:18.1"

// superuserUsername and superuserPassword are the credentials of the
// container's own postgres superuser, used only to bootstrap the two
// application roles (see roles.go) and to let pgtestdb create and drop
// per-test databases and templates. No test ever receives a pool
// connected as this role.
const (
	superuserUsername = "frappe_test_superuser"
	// superuserPassword is not a real credential: it only ever reaches a
	// disposable, container-local Postgres server started by this test
	// helper, the same way deployments/database/initialize.sql's
	// development-only passwords do.
	superuserPassword = "frappe_test_superuser_development_only" //nolint:gosec // test-only container credential, never a real secret.
	superuserDatabase = "frappe_test"
)

// serverAddress describes where to reach a running Postgres server.
type serverAddress struct {
	host string
	port string
}

var (
	sharedServerOnce  sync.Once //nolint:gochecknoglobals // one server lookup per test binary; the container is shared per "go test" invocation (see package doc).
	sharedServer      serverAddress
	sharedServerError error
)

// server returns the shared, process-wide Postgres server, starting and
// preparing it (container, roles) at most once. Every call after the
// first returns immediately with the same address, or the same error.
func server(ctx context.Context) (serverAddress, error) {
	sharedServerOnce.Do(func() {
		sharedServer, sharedServerError = startServer(ctx)
	})
	return sharedServer, sharedServerError
}

// startServer starts (or attaches to this "go test" invocation's) Postgres container tuned
// for disposable test workloads and bootstraps the two application
// roles. It is called at most once per process, by server, through
// sharedServerOnce.
func startServer(ctx context.Context) (serverAddress, error) {
	container, err := postgres.Run(ctx, containerImage,
		postgres.WithDatabase(superuserDatabase),
		postgres.WithUsername(superuserUsername),
		postgres.WithPassword(superuserPassword),
		postgres.BasicWaitStrategies(),
		testcontainers.WithReuseByName(containerNameForSession(testcontainers.SessionID())),
		// Tuned for parallel, disposable test workloads: durability
		// guarantees a real deployment needs are worthless here, since a
		// crash only means the next test run starts a fresh container,
		// and the raised connection limit accommodates every package's
		// tests running in parallel against the same shared container.
		testcontainers.WithCmdArgs(
			"-c", "fsync=off",
			"-c", "synchronous_commit=off",
			"-c", "full_page_writes=off",
			"-c", "max_connections=500", // see testPoolMaxConnections in pool.go.
		),
		// The postgres module does not set PGDATA, so the image default
		// applies: from postgres:18 on it is /var/lib/postgresql/18/docker,
		// no longer /var/lib/postgresql/data. Mounting tmpfs at the parent
		// /var/lib/postgresql covers PGDATA for this and later majors.
		testcontainers.WithTmpfs(map[string]string{
			"/var/lib/postgresql": "rw",
		}),
	)
	if err != nil {
		return serverAddress{}, fmt.Errorf("databasetest: start postgres container: %w", err)
	}

	host, err := container.Host(ctx)
	if err != nil {
		return serverAddress{}, fmt.Errorf("databasetest: get container host: %w", err)
	}
	port, err := container.MappedPort(ctx, "5432/tcp")
	if err != nil {
		return serverAddress{}, fmt.Errorf("databasetest: get container port: %w", err)
	}

	address := serverAddress{host: host, port: port.Port()}

	if err := bootstrapRoles(ctx, address); err != nil {
		return serverAddress{}, err
	}

	return address, nil
}
