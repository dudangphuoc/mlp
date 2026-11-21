-- mlp:query ListRuns :many
SELECT r.id, r.pipeline_id, p.name AS pipeline_name, r.workspace_id, r.number, r.state, r.params::text AS params, r.trigger, r.error, r.created_by, r.created_at, r.finished_at FROM pipeline.runs r JOIN pipeline.pipelines p ON p.id = r.pipeline_id
WHERE r.workspace_id = @workspace_id AND (@pipeline_id::uuid IS NULL OR r.pipeline_id = @pipeline_id) ORDER BY r.created_at DESC LIMIT 200;
