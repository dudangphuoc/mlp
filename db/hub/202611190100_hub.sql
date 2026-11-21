-- Hugging Face integration (M01 S-01-10, M04, M11, M17): workspace token (encrypted) + history of dataset/model imports from the HF Hub.
CREATE SCHEMA IF NOT EXISTS hub;

CREATE TABLE IF NOT EXISTS hub.integrations (
    workspace_id    uuid        NOT NULL,
    kind            text        NOT NULL,           -- huggingface
    secret_enc      text        NOT NULL,           -- AES-GCM (key derived from the instance pepper) — never returned to the client
    account         text,                           -- HF account name (whoami) at verification time
    status          text        NOT NULL DEFAULT 'unchecked',   -- ok | invalid | unchecked
    created_by      uuid        NOT NULL,
    updated_at      timestamptz NOT NULL,
    last_checked_at timestamptz,
    PRIMARY KEY (workspace_id, kind)
);

CREATE TABLE IF NOT EXISTS hub.imports (
    id           uuid PRIMARY KEY,
    workspace_id uuid        NOT NULL,
    kind         text        NOT NULL,              -- dataset | model
    repo         text        NOT NULL,
    revision     text        NOT NULL,
    include      text[]      NOT NULL DEFAULT '{}', -- glob; empty = all files
    target_id    uuid,                              -- dataset id / model version id
    target_name  text        NOT NULL,
    task         text        NOT NULL,
    state        text        NOT NULL,              -- queued | running | succeeded | failed
    files_total  int         NOT NULL DEFAULT 0,
    files_done   int         NOT NULL DEFAULT 0,
    bytes_total  bigint      NOT NULL DEFAULT 0,
    bytes_done   bigint      NOT NULL DEFAULT 0,
    message      text        NOT NULL DEFAULT '',
    created_by   uuid        NOT NULL,
    created_at   timestamptz NOT NULL,
    updated_at   timestamptz NOT NULL,
    finished_at  timestamptz
);
CREATE INDEX IF NOT EXISTS hub_imports_ws ON hub.imports (workspace_id, created_at DESC);
