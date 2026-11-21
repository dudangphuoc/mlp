-- Round 1 slice 3 (R15–R22): a trial is a hidden training on a small subset. It reuses the run machinery (job, volume, metrics,
-- log, model upload); the run is marked is_trial and numbered -1, -2, … per project so it never takes a visible #n.
ALTER TABLE track.runs ADD COLUMN is_trial boolean NOT NULL DEFAULT false;
ALTER TABLE track.runs ADD COLUMN trial_id uuid;   -- real run: the passed trial that opened the gate for it

CREATE TABLE track.trials (
    id                 uuid PRIMARY KEY,
    workspace_id       uuid        NOT NULL,
    project_id         uuid        NOT NULL REFERENCES track.projects(id),
    project_recipe_id  uuid        NOT NULL REFERENCES track.project_recipes(id),
    revision_id        uuid        NOT NULL REFERENCES track.revisions(id),
    run_id             uuid        NOT NULL,
    trial_key          text        NOT NULL,         -- revision content without the parameters that need no new trial
    dataset_version_id uuid,
    start_kind         text        NOT NULL,         -- pretrained | backbone
    host_id            uuid,                         -- machine the attempt ran on (set when it finishes)
    gpus               int         NOT NULL,
    state              text        NOT NULL,         -- queued | running | passed | failed | cancelled
    stages             jsonb       NOT NULL DEFAULT '[]',
    steps_per_s        double precision,
    peak_vram_mb       bigint,
    vram_estimated     boolean     NOT NULL DEFAULT false,
    val_s_per_batch    double precision,
    train_images       int         NOT NULL,
    val_images         int         NOT NULL,
    error              text        NOT NULL DEFAULT '',
    created_by         uuid        NOT NULL,
    created_at         timestamptz NOT NULL,
    finished_at        timestamptz
);
CREATE INDEX trials_match ON track.trials (project_recipe_id, trial_key, created_at DESC);
CREATE INDEX trials_run ON track.trials (run_id);
