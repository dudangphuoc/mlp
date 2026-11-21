-- mlp:query SyncedVersionsOnHosts :many
-- Whole versions of a dataset each machine synced into a cache volume that still exists (removal marks every row of the volume).
SELECT DISTINCT host_id, version_id
FROM ops.data_syncs
WHERE workspace_id = @workspace_id AND dataset_id = @dataset_id AND removed_at IS NULL AND NOT subset AND version_id IS NOT NULL;
