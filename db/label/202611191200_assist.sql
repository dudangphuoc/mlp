-- AI label proposals (Hermes phase 3). Sources a project may use, proposals per item (pending until a person applies them),
-- and batch pre-label jobs. External APIs (data leaves the platform) are opt-in per project and per action.
ALTER TABLE ai.connections ADD COLUMN IF NOT EXISTS is_external boolean NOT NULL DEFAULT false;
UPDATE ai.connections SET is_external = true WHERE provider_type IN ('openai', 'anthropic', 'deepseek', 'typesafe');

CREATE TABLE label.assist_settings (
    project_id     uuid PRIMARY KEY REFERENCES label.projects(id) ON DELETE CASCADE,
    workspace_id   uuid        NOT NULL,
    sources        jsonb       NOT NULL DEFAULT '[]',   -- [{kind: model|deployment, id}]
    class_mapping  jsonb       NOT NULL DEFAULT '{}',   -- {"model label": schema class id}
    allow_external boolean     NOT NULL DEFAULT false,
    min_confidence double precision NOT NULL DEFAULT 0.3,
    updated_by     uuid,
    updated_at     timestamptz NOT NULL
);

CREATE TABLE label.prelabel_jobs (
    id           uuid PRIMARY KEY,
    workspace_id uuid        NOT NULL,
    project_id   uuid        NOT NULL REFERENCES label.projects(id) ON DELETE CASCADE,
    source_kind  text        NOT NULL,
    source_id    uuid        NOT NULL,
    source_label text        NOT NULL,
    total        int         NOT NULL,
    done         int         NOT NULL DEFAULT 0,
    failed       int         NOT NULL DEFAULT 0,
    state        text        NOT NULL,               -- queued | running | succeeded | failed | cancelled
    error        text,
    created_by   uuid        NOT NULL,
    created_at   timestamptz NOT NULL,
    started_at   timestamptz,
    finished_at  timestamptz
);
CREATE INDEX ix_prelabel_jobs_project ON label.prelabel_jobs (project_id, created_at DESC);
CREATE INDEX ix_prelabel_jobs_open ON label.prelabel_jobs (state) WHERE state IN ('queued', 'running');

CREATE TABLE label.suggestion_sets (
    id           uuid PRIMARY KEY,
    workspace_id uuid        NOT NULL,
    project_id   uuid        NOT NULL REFERENCES label.projects(id) ON DELETE CASCADE,
    path         text        NOT NULL,
    revision     int         NOT NULL,
    source_kind  text        NOT NULL,               -- model | deployment
    source_id    uuid        NOT NULL,
    source_label text        NOT NULL,
    external     boolean     NOT NULL,
    state        text        NOT NULL,               -- proposed | failed
    detail       text        NOT NULL,
    items        jsonb       NOT NULL,               -- [{kind, class_id, geometry, confidence, label}]
    unmapped     jsonb       NOT NULL,               -- [{label, count}] — model labels with no schema class
    ai_run_id    uuid,
    latency_ms   int         NOT NULL,
    job_id       uuid REFERENCES label.prelabel_jobs(id) ON DELETE SET NULL,
    decision     text        NOT NULL DEFAULT 'pending',   -- pending | applied | dismissed
    created_by   uuid        NOT NULL,
    created_at   timestamptz NOT NULL,
    decided_by   uuid,
    decided_at   timestamptz
);
CREATE INDEX ix_suggestion_sets_item ON label.suggestion_sets (project_id, path, created_at DESC);
CREATE INDEX ix_suggestion_sets_job ON label.suggestion_sets (job_id) WHERE job_id IS NOT NULL;
