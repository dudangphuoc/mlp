-- Hermes label checks (phase 2). Every check is stored with its steps so people see exactly why an item passed, was flagged or failed,
-- and whether Hermes accepted it automatically (undoable).
ALTER TABLE label.projects ADD COLUMN IF NOT EXISTS hermes_enabled boolean NOT NULL DEFAULT false;

CREATE TABLE label.hermes_checks (
    id            uuid PRIMARY KEY,
    workspace_id  uuid        NOT NULL,
    project_id    uuid        NOT NULL REFERENCES label.projects(id) ON DELETE CASCADE,
    path          text        NOT NULL,
    revision      int         NOT NULL,
    trigger       text        NOT NULL,                 -- submit | manual
    verdict       text        NOT NULL,                 -- pass | flag | fail | error
    confidence    double precision,                     -- from the decision model; null = rules only
    decided_by    text        NOT NULL,                 -- rules | rules+model
    summary       text        NOT NULL,
    steps         jsonb       NOT NULL,                 -- [{name, kind, outcome, detail, ms}]
    suggestion    text,
    ai_run_id     uuid,
    action        text        NOT NULL DEFAULT 'none',  -- none | auto_accepted | undone
    actor_id      uuid,
    created_at    timestamptz NOT NULL,
    undone_by     uuid,
    undone_at     timestamptz
);
CREATE INDEX ix_hermes_checks_project ON label.hermes_checks (project_id, created_at DESC);
CREATE INDEX ix_hermes_checks_item ON label.hermes_checks (project_id, path, created_at DESC);
