-- mlp:query InsertDataSync :exec
INSERT INTO ops.data_syncs (id, workspace_id, job_id, attempt_id, host_id, dataset_id, files, reused_files, reused_bytes, fetched_files, fetched_bytes,
                            uncached_files, retries, seconds, cache_files, cache_bytes, free_bytes, at, version_id, subset)
VALUES (@id, @workspace_id, @job_id, @attempt_id, @host_id, @dataset_id, @files, @reused_files, @reused_bytes, @fetched_files, @fetched_bytes,
        @uncached_files, @retries, @seconds, @cache_files, @cache_bytes, @free_bytes, @at, @version_id, @subset);
