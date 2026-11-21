-- M10 §3 (first slice): grid/random sweeps; each trial is an M09 run (D-21); the coordinator runs in the control plane.
CREATE SCHEMA IF NOT EXISTS tune;

CREATE TABLE tune.sweeps (
    id                 uuid PRIMARY KEY,
    workspace_id       uuid        NOT NULL,
    project_id         uuid        NOT NULL,
    name               text        NOT NULL,
    recipe             text        NOT NULL,
    variant            text        NOT NULL,
    task               text        NOT NULL,
    algorithm          text        NOT NULL,      -- grid | random
    space              jsonb       NOT NULL,      -- {param: {values:[…]} | {min,max,log?,int?}}
    base_params        jsonb       NOT NULL,
    objective          text        NOT NULL,      -- metric key (defaults to the recipe's primary metric)
    direction          text        NOT NULL,      -- maximize | minimize
    max_trials         int         NOT NULL,
    parallel           int         NOT NULL,
    seed               int         NOT NULL,
    cpus               double precision NOT NULL,
    gpus               int         NOT NULL,
    dataset_version_id uuid,
    image_override     text,
    network            text,
    state              text        NOT NULL,      -- running | completed | stopped | failed
    state_reason       text,
    best_trial         int,
    best_value         double precision,
    created_by         uuid        NOT NULL,
    created_at         timestamptz NOT NULL,
    finished_at        timestamptz
);
CREATE INDEX ix_sweeps_ws ON tune.sweeps (workspace_id, created_at DESC);

CREATE TABLE tune.trials (
    sweep_id   uuid        NOT NULL REFERENCES tune.sweeps(id),
    number     int         NOT NULL,
    params     jsonb       NOT NULL,
    run_id     uuid,
    state      text        NOT NULL,             -- queued | running | succeeded | failed | cancelled
    value      double precision,
    error      text,
    created_at timestamptz NOT NULL,
    PRIMARY KEY (sweep_id, number)
);
