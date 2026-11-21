-- mlp:query ListHostDataCaches :many
-- What each machine holds: the latest sync per (host, dataset) whose volume was not deleted since (in use = its last job still active, set by the handler).
SELECT s.host_id, s.dataset_id, s.cache_files, s.cache_bytes, s.free_bytes, s.at AS last_used_at, s.job_id AS last_job_id
FROM (SELECT DISTINCT ON (host_id, dataset_id) *
      FROM ops.data_syncs
      WHERE workspace_id = @workspace_id AND (@host_id::uuid IS NULL OR host_id = @host_id)
      ORDER BY host_id, dataset_id, at DESC) s
WHERE s.removed_at IS NULL
ORDER BY s.at DESC;
