-- mlp:query DatasetCacheByHost :many
-- Bytes of one dataset each machine still holds (latest sync per host, volume not deleted) — the scheduler prefers those machines.
SELECT s.host_id, s.cache_bytes, s.cache_files
FROM (SELECT DISTINCT ON (host_id) host_id, cache_bytes, cache_files, removed_at
      FROM ops.data_syncs
      WHERE workspace_id = @workspace_id AND dataset_id = @dataset_id
      ORDER BY host_id, at DESC) s
WHERE s.removed_at IS NULL;
