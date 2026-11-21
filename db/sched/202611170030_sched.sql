-- M03 §3 (P0-15 part)
CREATE SCHEMA IF NOT EXISTS sched;

CREATE TABLE sched.jobs (
    id            uuid PRIMARY KEY,
    workspace_id  uuid        NOT NULL,
    kind          text        NOT NULL,
    name          text        NOT NULL,
    owner_id      uuid        NOT NULL,
    spec          jsonb       NOT NULL,
    priority      int         NOT NULL DEFAULT 50,
    state         text        NOT NULL,
    state_reason  text,
    max_attempts  int         NOT NULL DEFAULT 3,
    attempt_count int         NOT NULL DEFAULT 0,
    created_at    timestamptz NOT NULL,
    queued_at     timestamptz NOT NULL,
    started_at    timestamptz,
    finished_at   timestamptz
);
CREATE INDEX jobs_queue ON sched.jobs (priority DESC, queued_at) WHERE state = 'queued';
CREATE INDEX jobs_ws ON sched.jobs (workspace_id, created_at DESC);

CREATE TABLE sched.job_attempts (
    id            uuid PRIMARY KEY,
    job_id        uuid        NOT NULL REFERENCES sched.jobs(id),
    number        int         NOT NULL,
    token_hash    text        NOT NULL UNIQUE,
    host_ids      uuid[]      NOT NULL,
    bundles       jsonb       NOT NULL,
    state         text        NOT NULL,
    exit_code     int,
    exit_reason   text,
    message       text,
    started_at    timestamptz NOT NULL,
    finished_at   timestamptz
);

CREATE TABLE sched.attempt_allocations (
    attempt_id  uuid        NOT NULL REFERENCES sched.job_attempts(id),
    host_id     uuid        NOT NULL,
    gpu_uuids   text[]      NOT NULL,
    node_ip     text        NOT NULL,
    reported_at timestamptz NOT NULL,
    PRIMARY KEY (attempt_id, host_id)
);
