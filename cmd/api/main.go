// Command api starts the frappe-api HTTP server by building the
// application through the composition root and running it until it is
// asked to shut down.
package main

import (
	"context"
	"fmt"
	"os"

	"github.com/MathiasHilgert/frappe-api/internal/dependencies"
)

func main() {
	application := dependencies.NewApplication()

	if err := application.Run(context.Background()); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
