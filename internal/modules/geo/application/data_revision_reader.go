package application

import "context"

// DataRevisionReader reads the revision of the loaded geo snapshot
// (geo_data_versions), which changes with every seed migration.
type DataRevisionReader interface {
	DataRevision(ctx context.Context) (string, error)
}
