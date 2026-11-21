-- 20/10 (D-35, D-36) first slice: operational event stream, progress + heartbeat, two-tier checkpoints, suspect state.
CREATE SCHEMA IF NOT EXISTS ops;

CREATE TABLE ops.events (
    id            uuid PRIMARY KEY,
    at            timestamptz NOT NULL,          -- timestamp at the source
    received_at   timestamptz NOT NULL,
    workspace_id  uuid,
    source        text        NOT NULL,          -- api | scheduler | agent:<host> | holder | shim | system:<loop>
    resource_type text        NOT NULL,          -- job | attempt | host | system
    resource_id   uuid,
    kind          text        NOT NULL,
    severity      text        NOT NULL,          -- info | success | warning | error
    message       text        NOT NULL,
    data          jsonb       NOT NULL DEFAULT '{}'
);
CREATE INDEX ix_ops_events_res ON ops.events (resource_type, resource_id, at);
CREATE INDEX ix_ops_events_ws ON ops.events (workspace_id, at DESC);

ALTER TABLE sched.jobs ADD COLUMN progress jsonb;               -- {phase, current, total, unit, percent, rate, eta_seconds, message}
ALTER TABLE sched.jobs ADD COLUMN progress_at timestamptz;
ALTER TABLE sched.job_attempts ADD COLUMN last_heartbeat_at timestamptz;
ALTER TABLE sched.job_attempts ADD COLUMN suspect_until timestamptz;
ALTER TABLE sched.job_attempts ADD COLUMN resumed_from text;    -- "epoch 3 (local)" …

CREATE TABLE sched.checkpoints (
    id          uuid PRIMARY KEY,
    job_id      uuid        NOT NULL REFERENCES sched.jobs(id),
    attempt_id  uuid        NOT NULL,
    host_id     uuid        NOT NULL,
    step        bigint      NOT NULL,
    name        text        NOT NULL,      -- file name within {output} (relative path)
    location    text        NOT NULL,      -- local | durable
    s3_key      text,                      -- durable: mlp-artifacts/{ws}/runs/{run}/checkpoints/{step}/{name}
    size_bytes  bigint      NOT NULL,
    sha256      text        NOT NULL,
    created_at  timestamptz NOT NULL,
    UNIQUE (job_id, name, location)
);
