-- Round 1 slice 2 (D-44): which machine has which recipe image, and the cached CPU-only inspections of a recipe.
CREATE TABLE catalog.environments (
    id           uuid PRIMARY KEY,
    workspace_id uuid        NOT NULL,
    recipe_name  text        NOT NULL,
    env_tag      text        NOT NULL,              -- image tag (content hash of the recipe, shared scripts and platform layers)
    host_id      uuid        NOT NULL,
    state        text        NOT NULL CHECK (state IN ('building', 'ready', 'failed')),
    error        text        NOT NULL DEFAULT '',
    started_at   timestamptz NOT NULL,
    built_at     timestamptz,
    UNIQUE (workspace_id, env_tag, host_id)
);
CREATE INDEX environments_recipe ON catalog.environments (workspace_id, recipe_name);

CREATE TABLE catalog.inspections (
    id            uuid PRIMARY KEY,
    workspace_id  uuid        NOT NULL,
    recipe_name   text        NOT NULL,
    env_tag       text        NOT NULL,
    variant       text        NOT NULL,
    params_sha256 text        NOT NULL,             -- canonical JSON of the parameters marked affectsInspect
    num_classes   integer     NOT NULL,
    state         text        NOT NULL CHECK (state IN ('running', 'ready', 'failed')),
    error         text        NOT NULL DEFAULT '',
    overlay_yaml  text        NOT NULL DEFAULT '',
    config_json   text        NOT NULL DEFAULT '',
    blocks        jsonb       NOT NULL DEFAULT '[]',
    created_at    timestamptz NOT NULL,
    UNIQUE (workspace_id, env_tag, variant, params_sha256, num_classes)
);
