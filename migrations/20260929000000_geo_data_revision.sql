-- +goose Up
-- +goose StatementBegin
-- geo_data_versions records which revision of the geo snapshot is loaded,
-- in a single row. The geo API derives its HTTP ETags from it and reads
-- it once at startup, so a seed migration that changes geo data MUST
-- update this row (UPDATE geo_data_versions SET revision = '<new>') in
-- the same migration. It starts at the revision of the seed in
-- migrations/geo.go ("geo-seed-3").
CREATE TABLE geo_data_versions (
    singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
    revision text NOT NULL CHECK (revision <> ''),
    updated_at timestamptz NOT NULL DEFAULT now()
);
-- +goose StatementEnd

-- +goose StatementBegin
INSERT INTO geo_data_versions (revision) VALUES ('geo-seed-3');
-- +goose StatementEnd

-- +goose StatementBegin
-- Reference data: the application only reads it.
REVOKE ALL ON geo_data_versions FROM frappe_application;
-- +goose StatementEnd

-- +goose StatementBegin
GRANT SELECT ON geo_data_versions TO frappe_application;
-- +goose StatementEnd

-- +goose Down
-- +goose StatementBegin
DROP TABLE geo_data_versions;
-- +goose StatementEnd
