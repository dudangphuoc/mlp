-- M09 §3 (first slice; TimescaleDB hypertable added once the extension is enabled)
CREATE SCHEMA IF NOT EXISTS track;

CREATE TABLE track.projects (
    id           uuid PRIMARY KEY,
    workspace_id uuid        NOT NULL,
    slug         text        NOT NULL,
    name         text        NOT NULL,
    task         text        NOT NULL,
    created_by   uuid        NOT NULL,
    created_at   timestamptz NOT NULL,
    UNIQUE (workspace_id, slug)
);

CREATE TABLE track.runs (
    id                   uuid PRIMARY KEY,
    project_id           uuid        NOT NULL REFERENCES track.projects(id),
    workspace_id         uuid        NOT NULL,
    name                 text        NOT NULL,
    kind                 text        NOT NULL,
    state                text        NOT NULL,
    job_id               uuid,
    user_id              uuid        NOT NULL,
    recipe               text        NOT NULL,
    variant              text        NOT NULL,
    task                 text        NOT NULL,
    params               jsonb       NOT NULL,
    primary_metric_key   text        NOT NULL,
    primary_metric_value double precision,
    created_at           timestamptz NOT NULL,
    ended_at             timestamptz
);
CREATE INDEX runs_project ON track.runs (project_id, created_at DESC);
CREATE INDEX runs_job ON track.runs (job_id);

CREATE TABLE track.metrics (
    run_id uuid             NOT NULL REFERENCES track.runs(id),
    key    text             NOT NULL,
    step   bigint           NOT NULL,
    at     timestamptz      NOT NULL,
    value  double precision NOT NULL,
    PRIMARY KEY (run_id, key, step)
);
