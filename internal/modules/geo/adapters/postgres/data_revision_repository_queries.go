package postgres

// dataRevisionQuery is the revision of the loaded geo snapshot.
const dataRevisionQuery = `-- name: geo.data_revision
SELECT revision FROM geo_data_versions`
