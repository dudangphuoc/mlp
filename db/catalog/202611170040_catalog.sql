-- M17 §3 (catalog part; build/zoo/ack added later)
CREATE SCHEMA IF NOT EXISTS catalog;

CREATE TABLE catalog.tasks (
    key            text PRIMARY KEY,
    modality_in    text   NOT NULL,
    modality_out   text   NOT NULL,
    converters     text[] NOT NULL,
    primary_metric text   NOT NULL,
    metrics        text[] NOT NULL
);

CREATE TABLE catalog.recipes (
    id              uuid PRIMARY KEY,
    workspace_id    uuid,                         -- null = builtin
    name            text        NOT NULL,
    display_name    text        NOT NULL,
    family          text        NOT NULL,
    description     text        NOT NULL,
    maturity        text        NOT NULL,
    tasks           text[]      NOT NULL,
    license_code    text        NOT NULL,
    license_weights text        NOT NULL,
    license_notes   text        NOT NULL,
    requires_ack    boolean     NOT NULL,
    gpu             text        NOT NULL,
    min_vram_gb     double precision NOT NULL,
    source          text        NOT NULL,
    manifest        jsonb       NOT NULL,
    manifest_sha256 text        NOT NULL,
    state           text        NOT NULL,
    selfcheck       text[]      NOT NULL,
    enabled         boolean     NOT NULL DEFAULT true,
    updated_at      timestamptz NOT NULL
);
CREATE UNIQUE INDEX recipes_name ON catalog.recipes (coalesce(workspace_id, '00000000-0000-0000-0000-000000000000'::uuid), name);
