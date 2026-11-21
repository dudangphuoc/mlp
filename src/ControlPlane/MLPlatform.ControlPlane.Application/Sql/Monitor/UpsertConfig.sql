-- mlp:query UpsertConfig :exec
INSERT INTO monitor.configs (deployment_id, workspace_id, dataset_version_id, label_column, baseline, baseline_rows, psi_warn, psi_alert,
  window_size, min_samples, enabled, updated_by, updated_at)
VALUES (@deployment_id, @workspace_id, @dataset_version_id, @label_column, @baseline::jsonb, @baseline_rows, @psi_warn, @psi_alert,
  @window_size, @min_samples, @enabled, @updated_by, @now)
ON CONFLICT (deployment_id) DO UPDATE SET dataset_version_id = excluded.dataset_version_id, label_column = excluded.label_column,
  baseline = excluded.baseline, baseline_rows = excluded.baseline_rows, psi_warn = excluded.psi_warn, psi_alert = excluded.psi_alert,
  window_size = excluded.window_size, min_samples = excluded.min_samples, enabled = excluded.enabled,
  updated_by = excluded.updated_by, updated_at = excluded.updated_at;
