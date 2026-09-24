-- +goose Up
-- +goose StatementBegin
-- Grants the application role (frappe_application) exactly the
-- privileges it needs at runtime on every object the migration role
-- (frappe_migration) creates from now on, without ever making
-- frappe_application the owner of anything. ALTER DEFAULT PRIVILEGES only
-- affects objects created after this statement runs, by the role that
-- runs it, so it must be applied early and every future migration keeps
-- running as the same migration role.
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO frappe_application;
-- +goose StatementEnd

-- +goose StatementBegin
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO frappe_application;
-- +goose StatementEnd

-- +goose StatementBegin
GRANT USAGE ON SCHEMA public TO frappe_application;
-- +goose StatementEnd

-- +goose Down
-- +goose StatementBegin
REVOKE USAGE ON SCHEMA public FROM frappe_application;
-- +goose StatementEnd

-- +goose StatementBegin
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    REVOKE USAGE, SELECT ON SEQUENCES FROM frappe_application;
-- +goose StatementEnd

-- +goose StatementBegin
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLES FROM frappe_application;
-- +goose StatementEnd
