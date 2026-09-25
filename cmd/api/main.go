// Command api starts the frappe-api HTTP server by building the
// application through the composition root and running it until it is
// asked to shut down.
package main

import (
	"context"
	"fmt"
	"os"

	"github.com/MathiasHilgert/frappe-api/internal/dependencies"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration/environment"
)

func main() {
	ctx := context.Background()

	application, err := dependencies.NewApplication(ctx, environment.New())
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}

	if err := application.Run(ctx); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
