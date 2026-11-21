-- M11 §3 (first slice)
CREATE SCHEMA IF NOT EXISTS registry;

CREATE TABLE registry.models (
    id           uuid PRIMARY KEY,
    workspace_id uuid        NOT NULL,
    name         text        NOT NULL,
    task         text        NOT NULL,
    description  text        NOT NULL,
    created_by   uuid        NOT NULL,
    created_at   timestamptz NOT NULL,
    UNIQUE (workspace_id, name)
);

CREATE TABLE registry.versions (
    id          uuid PRIMARY KEY,
    model_id    uuid        NOT NULL REFERENCES registry.models(id),
    version     int         NOT NULL,
    run_id      uuid,
    recipe      text        NOT NULL,
    variant     text        NOT NULL,
    task        text        NOT NULL,
    prefix      text        NOT NULL,        -- mlp-artifacts/{ws}/models/{model}/v{n}/ (D-28 model directory)
    file_count  bigint      NOT NULL,
    size_bytes  bigint      NOT NULL,
    metrics     jsonb       NOT NULL,
    stage       text        NOT NULL DEFAULT 'none',
    created_by  uuid        NOT NULL,
    created_at  timestamptz NOT NULL,
    UNIQUE (model_id, version)
);

CREATE TABLE registry.stage_transitions (
    id         uuid PRIMARY KEY,
    version_id uuid        NOT NULL REFERENCES registry.versions(id),
    from_stage text        NOT NULL,
    to_stage   text        NOT NULL,
    actor_id   uuid        NOT NULL,
    reason     text        NOT NULL,
    at         timestamptz NOT NULL
);
