-- Round 1 (D-49): a project works on its own copy of a recipe. People edit a draft; a training (later also a trial) freezes the draft
-- into an immutable revision, and the run remembers which revision it cooked.
CREATE TABLE track.project_recipes (
    id                     uuid PRIMARY KEY,
    workspace_id           uuid        NOT NULL,
    project_id             uuid        NOT NULL REFERENCES track.projects(id),
    origin_recipe_id       uuid        NOT NULL,
    origin_name            text        NOT NULL,
    origin_manifest        jsonb       NOT NULL,     -- the Catalog manifest as it was when taken (kept to diff against later changes)
    origin_manifest_sha256 text        NOT NULL,
    created_by             uuid        NOT NULL,
    created_at             timestamptz NOT NULL,
    UNIQUE (project_id, origin_name)
);

CREATE TABLE track.revisions (
    id                 uuid PRIMARY KEY,
    workspace_id       uuid        NOT NULL,
    project_recipe_id  uuid        NOT NULL REFERENCES track.project_recipes(id),
    number             int         NOT NULL,
    parent_revision_id uuid,
    manifest           jsonb       NOT NULL,
    manifest_sha256    text        NOT NULL,
    variant            text        NOT NULL,
    params             jsonb       NOT NULL,         -- only the values that were set; a missing key = the manifest default
    content_sha256     text        NOT NULL,
    trial_key          text        NOT NULL,         -- content without the parameters that need no new trial (epochs, seed)
    created_by         uuid        NOT NULL,
    created_at         timestamptz NOT NULL,
    UNIQUE (project_recipe_id, number),
    UNIQUE (project_recipe_id, content_sha256)
);

CREATE TABLE track.drafts (
    id                 uuid PRIMARY KEY,
    workspace_id       uuid        NOT NULL,
    project_recipe_id  uuid        NOT NULL REFERENCES track.project_recipes(id),
    user_id            uuid        NOT NULL,
    base_revision_id   uuid,
    variant            text        NOT NULL,
    params             jsonb       NOT NULL,
    dataset_version_id uuid,
    start_kind         text        NOT NULL,         -- pretrained | backbone
    train_kind         text        NOT NULL,         -- fine_tune | retrain
    retrain_of_run_id  uuid,
    host_id            uuid,
    gpus               int         NOT NULL,
    cpus               double precision NOT NULL,
    run_name           text        NOT NULL,
    etag               int         NOT NULL DEFAULT 0,
    updated_at         timestamptz NOT NULL,
    UNIQUE (project_recipe_id, user_id)
);

ALTER TABLE track.runs ADD COLUMN revision_id uuid;
ALTER TABLE track.runs ADD COLUMN train_kind text;
ALTER TABLE track.runs ADD COLUMN start_kind text;
ALTER TABLE track.runs ADD COLUMN retrain_of_run_id uuid;
ALTER TABLE track.runs ADD COLUMN host_id uuid;
