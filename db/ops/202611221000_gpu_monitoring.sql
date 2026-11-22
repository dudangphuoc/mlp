-- GPU monitoring (docs/agent-upgrade.md §2): raw samples every 15 s kept 24 h, 1-minute rollups kept 30 days.
CREATE SCHEMA IF NOT EXISTS ops;

CREATE TABLE IF NOT EXISTS ops.gpu_samples (
    host_id     uuid        NOT NULL,
    gpu_uuid    text        NOT NULL,
    at          timestamptz NOT NULL,
    util        real        NOT NULL,
    mem_util    real,
    vram_used   bigint      NOT NULL,
    vram_total  bigint      NOT NULL,
    temp        real        NOT NULL,
    power       real        NOT NULL,
    power_limit real,
    sm_clock    real,
    throttle    text[]      NOT NULL DEFAULT '{}',
    ecc         bigint,
    PRIMARY KEY (host_id, gpu_uuid, at)
);
CREATE INDEX IF NOT EXISTS gpu_samples_at ON ops.gpu_samples (at);

CREATE TABLE IF NOT EXISTS ops.gpu_samples_1m (
    host_id    uuid        NOT NULL,
    gpu_uuid   text        NOT NULL,
    at         timestamptz NOT NULL,
    util       real        NOT NULL,
    vram_used  bigint      NOT NULL,
    vram_total bigint      NOT NULL,
    temp       real        NOT NULL,
    power      real        NOT NULL,
    sm_clock   real,
    PRIMARY KEY (host_id, gpu_uuid, at)
);
CREATE INDEX IF NOT EXISTS gpu_samples_1m_at ON ops.gpu_samples_1m (at);

-- One row per active condition; notified once it has held for the debounce window; deleted when it clears.
CREATE TABLE IF NOT EXISTS ops.gpu_alerts (
    host_id     uuid        NOT NULL,
    gpu_uuid    text        NOT NULL,
    kind        text        NOT NULL,
    level       text        NOT NULL,
    since       timestamptz NOT NULL,
    notified_at timestamptz,
    PRIMARY KEY (host_id, gpu_uuid, kind)
);
