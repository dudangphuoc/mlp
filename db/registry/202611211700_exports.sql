-- Model conversion (export): a job runs the recipe's `entrypoints.export` for one model version and one format; the result is one file
-- (or one zip when the format writes several) in object storage, downloadable from the Models page.
CREATE TABLE registry.exports (
    id               uuid PRIMARY KEY,
    workspace_id     uuid        NOT NULL,
    model_version_id uuid        NOT NULL REFERENCES registry.versions(id),
    format           text        NOT NULL,
    state            text        NOT NULL CHECK (state IN ('running', 'ready', 'failed')),
    error            text        NOT NULL DEFAULT '',
    job_id           uuid,
    file_name        text        NOT NULL DEFAULT '',
    size_bytes       bigint      NOT NULL DEFAULT 0,
    created_by       uuid        NOT NULL,
    created_at       timestamptz NOT NULL
);
CREATE INDEX ix_exports_version ON registry.exports (model_version_id, created_at DESC);
