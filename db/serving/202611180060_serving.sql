-- M12 §3 (first slice)
CREATE SCHEMA IF NOT EXISTS serving;

CREATE TABLE serving.deployments (
    id               uuid PRIMARY KEY,
    workspace_id     uuid        NOT NULL,
    slug             text        NOT NULL,
    model_version_id uuid        NOT NULL,
    model_name       text        NOT NULL,
    model_version    int         NOT NULL,
    task             text        NOT NULL,
    state            text        NOT NULL,
    state_reason     text,
    job_id           uuid,
    endpoint         text,                       -- replica host:port (ModelServer)
    created_by       uuid        NOT NULL,
    created_at       timestamptz NOT NULL,
    UNIQUE (workspace_id, slug)
);
CREATE INDEX deployments_job ON serving.deployments (job_id);

CREATE TABLE serving.endpoint_keys (
    id            uuid PRIMARY KEY,
    deployment_id uuid        NOT NULL REFERENCES serving.deployments(id),
    name          text        NOT NULL,
    prefix        text        NOT NULL,
    hash          text        NOT NULL UNIQUE,
    created_by    uuid        NOT NULL,
    created_at    timestamptz NOT NULL,
    revoked_at    timestamptz
);
