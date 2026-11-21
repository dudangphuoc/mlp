-- mlp:query ListJobDataSyncs :many
SELECT id, job_id, attempt_id, host_id, dataset_id, files, reused_files, reused_bytes, fetched_files, fetched_bytes, uncached_files, retries,
       seconds, cache_files, cache_bytes, free_bytes, at
FROM ops.data_syncs
WHERE workspace_id = @workspace_id AND job_id = @job_id
ORDER BY at DESC
LIMIT 20;
