-- mlp:query ListEnabledConfigs :many
SELECT c.deployment_id, c.workspace_id, c.dataset_version_id, c.label_column, c.baseline::text AS baseline, c.baseline_rows, c.psi_warn, c.psi_alert,
  c.window_size, c.min_samples, c.enabled, c.updated_at
FROM monitor.configs c JOIN serving.deployments d ON d.id = c.deployment_id
WHERE c.enabled AND c.baseline IS NOT NULL AND d.state IN ('healthy', 'degraded');
