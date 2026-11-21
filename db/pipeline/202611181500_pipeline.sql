-- M14 §3 (first slice): pipeline DAG declared as JSON; engine runs in the control plane; each train/job step is a regular M03/M09 job.
CREATE SCHEMA IF NOT EXISTS pipeline;

CREATE TABLE pipeline.pipelines (
    id                uuid PRIMARY KEY,
    workspace_id      uuid        NOT NULL,
    name              text        NOT NULL,
    definition        jsonb       NOT NULL,     -- {params:{…}, steps:[{id, kind, needs[], config{…}, retries}]}
    trigger           jsonb       NOT NULL,     -- {type: manual | interval | event, minutes?, event?}
    enabled           boolean     NOT NULL DEFAULT true,
    last_triggered_at timestamptz,
    created_by        uuid        NOT NULL,
    created_at        timestamptz NOT NULL,
    updated_at        timestamptz NOT NULL,
    UNIQUE (workspace_id, name)
);

CREATE TABLE pipeline.runs (
    id           uuid PRIMARY KEY,
    pipeline_id  uuid        NOT NULL REFERENCES pipeline.pipelines(id),
    workspace_id uuid        NOT NULL,
    number       int         NOT NULL,
    state        text        NOT NULL,      -- running | succeeded | gated | failed | cancelled
    params       jsonb       NOT NULL,
    trigger      text        NOT NULL,      -- manual | interval | event:<kind> | retry:<run>
    error        text,
    created_by   uuid        NOT NULL,
    created_at   timestamptz NOT NULL,
    finished_at  timestamptz,
    UNIQUE (pipeline_id, number)
);
CREATE INDEX ix_pipeline_runs_state ON pipeline.runs (state);

CREATE TABLE pipeline.steps (
    run_id      uuid        NOT NULL REFERENCES pipeline.runs(id),
    step_id     text        NOT NULL,
    kind        text        NOT NULL,
    state       text        NOT NULL,       -- pending | running | succeeded | failed | skipped | gated
    attempt     int         NOT NULL DEFAULT 0,
    ref_id      uuid,                       -- run M09 / job M03 / model version …
    output      jsonb       NOT NULL DEFAULT '{}',
    error       text,
    started_at  timestamptz,
    finished_at timestamptz,
    PRIMARY KEY (run_id, step_id)
);
