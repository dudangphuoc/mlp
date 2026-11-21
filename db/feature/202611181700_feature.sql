-- M07 §3 (first slice): entities, versioned feature views, offline/online store in Postgres (Parquet + Redis + Ray Data later),
-- deployment → feature bindings so the gateway uses the right definitions at inference time.
CREATE SCHEMA IF NOT EXISTS feature;

CREATE TABLE feature.entities (
    id           uuid PRIMARY KEY,
    workspace_id uuid        NOT NULL,
    name         text        NOT NULL,
    join_key     text        NOT NULL,
    description  text        NOT NULL,
    created_by   uuid        NOT NULL,
    created_at   timestamptz NOT NULL,
    UNIQUE (workspace_id, name)
);

CREATE TABLE feature.views (
    id                    uuid PRIMARY KEY,
    workspace_id          uuid        NOT NULL,
    name                  text        NOT NULL,
    version               int         NOT NULL,
    entity_id             uuid        NOT NULL REFERENCES feature.entities(id),
    source_version_id     uuid        NOT NULL,      -- dataset version (tabular CSV)
    timestamp_column      text        NOT NULL,
    features              jsonb       NOT NULL,      -- [{name, type: number|string}]
    ttl_hours             int         NOT NULL,
    online                boolean     NOT NULL,
    materialize_every_min int,                       -- null = manual runs only
    state                 text        NOT NULL,      -- pending | ready | failed
    state_reason          text,
    rows                  bigint      NOT NULL DEFAULT 0,
    entities              bigint      NOT NULL DEFAULT 0,
    last_materialized_at  timestamptz,
    max_event_ts          timestamptz,
    created_by            uuid        NOT NULL,
    created_at            timestamptz NOT NULL,
    UNIQUE (workspace_id, name, version)
);

CREATE TABLE feature.offline (
    view_id    uuid        NOT NULL REFERENCES feature.views(id),
    entity_key text        NOT NULL,
    event_ts   timestamptz NOT NULL,
    values     jsonb       NOT NULL
);
CREATE INDEX ix_offline_pit ON feature.offline (view_id, entity_key, event_ts DESC);

CREATE TABLE feature.online (
    view_id    uuid        NOT NULL REFERENCES feature.views(id),
    entity_key text        NOT NULL,
    event_ts   timestamptz NOT NULL,
    values     jsonb       NOT NULL,
    updated_at timestamptz NOT NULL,
    PRIMARY KEY (view_id, entity_key)
);

CREATE TABLE feature.bindings (
    deployment_id uuid PRIMARY KEY,
    workspace_id  uuid        NOT NULL,
    entity_id     uuid        NOT NULL REFERENCES feature.entities(id),
    features      jsonb       NOT NULL,              -- ["view:feature", "view@2:feature", …]
    updated_by    uuid        NOT NULL,
    updated_at    timestamptz NOT NULL
);
