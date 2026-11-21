-- Model evaluation on a dataset version (playground step 2): the platform calls a replica of the model version for each
-- sample and computes the task metrics itself, so every servable model can be evaluated (D-25), with per-sample results.
CREATE TABLE registry.evaluations (
    id                 uuid PRIMARY KEY,
    workspace_id       uuid        NOT NULL,
    model_version_id   uuid        NOT NULL REFERENCES registry.versions(id),
    dataset_version_id uuid        NOT NULL,
    path_filter        text        NOT NULL,
    max_samples        int         NOT NULL,
    conf_threshold     double precision NOT NULL,
    deployment_id      uuid,
    state              text        NOT NULL,          -- queued | preparing | running | succeeded | failed | cancelled
    state_reason       text,
    total              int         NOT NULL DEFAULT 0,
    done               int         NOT NULL DEFAULT 0,
    errors             int         NOT NULL DEFAULT 0,
    metrics            jsonb       NOT NULL DEFAULT '{}',
    per_class          jsonb       NOT NULL DEFAULT '[]',
    confusion          jsonb,
    created_by         uuid        NOT NULL,
    created_at         timestamptz NOT NULL,
    started_at         timestamptz,
    finished_at        timestamptz
);
CREATE INDEX ix_evaluations_version ON registry.evaluations (model_version_id, created_at DESC);

CREATE TABLE registry.evaluation_samples (
    evaluation_id uuid        NOT NULL REFERENCES registry.evaluations(id) ON DELETE CASCADE,
    idx           int         NOT NULL,
    path          text        NOT NULL,
    width         int         NOT NULL DEFAULT 0,
    height        int         NOT NULL DEFAULT 0,
    gt            jsonb       NOT NULL,           -- detect: [{c, b:[x1,y1,x2,y2]}]; classify: {c}
    pred          jsonb       NOT NULL,           -- detect: [{c, s, b}];          classify: {c, s, top:[{c,s}]}
    tp            int         NOT NULL DEFAULT 0,
    fp            int         NOT NULL DEFAULT 0,
    fn            int         NOT NULL DEFAULT 0,
    correct       boolean     NOT NULL DEFAULT false,
    error         text,
    latency_ms    double precision NOT NULL DEFAULT 0,
    PRIMARY KEY (evaluation_id, idx)
);
