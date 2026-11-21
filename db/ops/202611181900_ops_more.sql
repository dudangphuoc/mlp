-- 20/10 remainder: per-attempt metrics (container/GPU), latest machine metrics, lost-signal / stalled flags.
CREATE TABLE ops.attempt_metrics (
    attempt_id   uuid        NOT NULL,
    at           timestamptz NOT NULL,
    cpu_percent  double precision NOT NULL,
    memory_bytes bigint      NOT NULL,
    gpu_util     double precision,
    vram_bytes   bigint
);
CREATE INDEX ix_attempt_metrics ON ops.attempt_metrics (attempt_id, at DESC);

ALTER TABLE fleet.hosts ADD COLUMN last_metrics jsonb;
ALTER TABLE fleet.hosts ADD COLUMN metrics_at timestamptz;
ALTER TABLE sched.jobs ADD COLUMN progress_changed_at timestamptz;
ALTER TABLE sched.jobs ADD COLUMN stalled_at timestamptz;
ALTER TABLE sched.job_attempts ADD COLUMN signal_lost_at timestamptz;
