-- M02 §3
CREATE SCHEMA IF NOT EXISTS fleet;

CREATE TABLE fleet.enrollments (
    id           uuid PRIMARY KEY,
    workspace_id uuid        NOT NULL,
    token_hash   text        NOT NULL UNIQUE,
    os           text        NOT NULL,
    data_path    text        NOT NULL,
    models_path  text        NOT NULL,
    created_by   uuid        NOT NULL,
    created_at   timestamptz NOT NULL,
    expires_at   timestamptz NOT NULL,
    used_at      timestamptz,
    host_id      uuid
);

CREATE SEQUENCE fleet.overlay_seq START 2;   -- 10.88.0.1 is the hub

CREATE TABLE fleet.hosts (
    id              uuid PRIMARY KEY,
    workspace_id    uuid        NOT NULL,
    name            text        NOT NULL,
    state           text        NOT NULL,
    state_reason    text,
    overlay_ip      inet        NOT NULL UNIQUE,
    wg_public_key   text,
    credential_hash text        NOT NULL UNIQUE,
    agent_version   text        NOT NULL,
    data_path       text        NOT NULL,
    models_path     text        NOT NULL,
    host_info       bytea,
    created_at      timestamptz NOT NULL,
    last_seen_at    timestamptz NOT NULL,
    disconnected_at timestamptz
);
CREATE INDEX hosts_ws ON fleet.hosts (workspace_id);
