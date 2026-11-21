-- Agent OTA (D-38): durable upgrade history (blogic-tunnels keeps it in memory — lost on restart).
CREATE TABLE IF NOT EXISTS fleet.agent_upgrades (
    id            uuid PRIMARY KEY,
    workspace_id  uuid        NOT NULL,
    host_id       uuid        NOT NULL REFERENCES fleet.hosts(id),
    from_version  text        NOT NULL,
    to_version    text        NOT NULL,
    state         text        NOT NULL,            -- queued | running | succeeded | failed | skipped
    phase         text        NOT NULL DEFAULT '',
    message       text        NOT NULL DEFAULT '',
    created_by    uuid,
    created_at    timestamptz NOT NULL,
    updated_at    timestamptz NOT NULL,
    finished_at   timestamptz
);
CREATE INDEX IF NOT EXISTS agent_upgrades_ws ON fleet.agent_upgrades (workspace_id, created_at DESC);
CREATE UNIQUE INDEX IF NOT EXISTS agent_upgrades_one_active ON fleet.agent_upgrades (host_id) WHERE state IN ('queued', 'running');
