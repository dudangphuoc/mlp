-- mlp:query ListModels :many
SELECT m.id, m.workspace_id, m.name, m.task, m.created_at,
       coalesce((SELECT max(v.version) FROM registry.versions v WHERE v.model_id = m.id), 0) AS latest_version,
       coalesce((SELECT max(v.version) FROM registry.versions v WHERE v.model_id = m.id AND v.stage = 'production'), 0) AS production_version
FROM registry.models m WHERE m.workspace_id = @workspace_id ORDER BY m.created_at DESC;
