-- mlp:query ListVersionsByRun :many
SELECT v.id, v.model_id, m.name AS model_name, v.version, v.run_id, v.recipe, v.variant, v.task, v.stage, v.file_count, v.size_bytes,
       v.metrics::text AS metrics, v.created_at, v.source, v.source_ref, v.storage, v.parent_version_id, v.optimization::text AS optimization
FROM registry.versions v JOIN registry.models m ON m.id = v.model_id
WHERE v.run_id = @run_id AND m.workspace_id = @workspace_id ORDER BY v.created_at DESC;
