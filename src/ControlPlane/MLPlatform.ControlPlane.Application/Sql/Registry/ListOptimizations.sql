-- mlp:query ListOptimizations :many
SELECT o.id, o.workspace_id, o.source_version_id, o.export_id, o.device, array_to_string(o.candidates, ',') AS candidates, o.host_id, o.calib_dataset_version_id, o.check_dataset_version_id, o.conf, o.state, o.error, o.job_id, o.report::text AS report, o.promoted::text AS promoted, o.created_at, m.name AS model_name, v.version AS source_version, v.task
FROM registry.optimizations o JOIN registry.versions v ON v.id = o.source_version_id JOIN registry.models m ON m.id = v.model_id
WHERE o.workspace_id = @workspace_id ORDER BY o.created_at DESC LIMIT 100;
