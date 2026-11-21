-- mlp:query UpdateDraft :scalar
-- Optimistic write: returns 0 when the draft was changed elsewhere since @etag was read.
WITH u AS (
  UPDATE track.drafts SET base_revision_id = @base_revision_id, variant = @variant, params = @params_json::jsonb, dataset_version_id = @dataset_version_id,
         start_kind = @start_kind, train_kind = @train_kind, retrain_of_run_id = @retrain_of_run_id, host_id = @host_id, gpus = @gpus, cpus = @cpus,
         run_name = @run_name, etag = etag + 1, updated_at = @now
  WHERE id = @id AND workspace_id = @workspace_id AND (@etag < 0 OR etag = @etag)
  RETURNING 1)
SELECT count(*) FROM u;
