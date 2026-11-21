-- mlp:query ListAllDataCaches :many
-- System janitor (every workspace): the latest sync per (workspace, host, dataset) whose volume still exists, oldest use first.
SELECT s.workspace_id, s.host_id, s.dataset_id, s.cache_bytes, s.free_bytes, s.at AS last_used_at, s.job_id AS last_job_id
FROM (SELECT DISTINCT ON (workspace_id, host_id, dataset_id) workspace_id, host_id, dataset_id, cache_bytes, free_bytes, at, job_id, removed_at
      FROM ops.data_syncs
      ORDER BY workspace_id, host_id, dataset_id, at DESC) s
WHERE s.removed_at IS NULL
ORDER BY s.at;
