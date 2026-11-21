-- Model optimization (v1): one run = one source version, one device (cpu | cuda), several candidates (fp32 | fp16 | int8) built,
-- checked against the source on the same images and benchmarked on the same machine. A chosen candidate becomes a model version.
CREATE TABLE registry.optimizations (
    id                       uuid PRIMARY KEY,
    workspace_id             uuid        NOT NULL,
    source_version_id        uuid        NOT NULL REFERENCES registry.versions(id),
    export_id                uuid        NOT NULL,
    device                   text        NOT NULL CHECK (device IN ('cpu', 'cuda')),
    candidates               text[]      NOT NULL,
    host_id                  uuid,
    calib_dataset_version_id uuid,
    check_dataset_version_id uuid        NOT NULL,
    conf                     double precision NOT NULL,
    state                    text        NOT NULL CHECK (state IN ('running', 'ready', 'failed')),
    error                    text        NOT NULL DEFAULT '',
    job_id                   uuid,
    report                   jsonb       NOT NULL DEFAULT '{}',
    promoted                 jsonb       NOT NULL DEFAULT '{}',   -- candidate → model version id
    created_by               uuid        NOT NULL,
    created_at               timestamptz NOT NULL
);
CREATE INDEX ix_optimizations_ws ON registry.optimizations (workspace_id, created_at DESC);

-- Lineage of a version made by an optimization: which version it was derived from, and how (device, precision, run id).
ALTER TABLE registry.versions ADD COLUMN IF NOT EXISTS parent_version_id uuid REFERENCES registry.versions(id);
ALTER TABLE registry.versions ADD COLUMN IF NOT EXISTS optimization jsonb;
