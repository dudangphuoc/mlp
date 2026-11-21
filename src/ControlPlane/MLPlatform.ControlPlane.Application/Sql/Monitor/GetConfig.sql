-- mlp:query GetConfig :one
SELECT deployment_id, workspace_id, dataset_version_id, label_column, baseline::text AS baseline, baseline_rows, psi_warn, psi_alert,
  window_size, min_samples, enabled, updated_at
FROM monitor.configs WHERE deployment_id = @deployment_id;
