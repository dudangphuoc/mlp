-- Host dataset cache: one row per finished dataset sync of an attempt (reported by the shim at the end of "preparing data").
-- The latest row per (host, dataset) describes what that machine holds; removed_at is set when the cache volume is deleted.
CREATE TABLE ops.data_syncs (
    id             uuid PRIMARY KEY,
    workspace_id   uuid             NOT NULL,
    job_id         uuid             NOT NULL,
    attempt_id     uuid             NOT NULL,
    host_id        uuid             NOT NULL,
    dataset_id     uuid             NOT NULL,
    files          int              NOT NULL,
    reused_files   int              NOT NULL,
    reused_bytes   bigint           NOT NULL,
    fetched_files  int              NOT NULL,
    fetched_bytes  bigint           NOT NULL,
    uncached_files int              NOT NULL,
    retries        int              NOT NULL,
    seconds        double precision NOT NULL,
    cache_files    bigint           NOT NULL,
    cache_bytes    bigint           NOT NULL,
    free_bytes     bigint           NOT NULL,
    at             timestamptz      NOT NULL,
    removed_at     timestamptz
);
CREATE INDEX ix_data_syncs_job ON ops.data_syncs (workspace_id, job_id, at DESC);
CREATE INDEX ix_data_syncs_host ON ops.data_syncs (workspace_id, host_id, dataset_id, at DESC);
