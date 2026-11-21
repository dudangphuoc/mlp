-- mlp:query GetRun :one
SELECT r.id, r.pipeline_id, p.name AS pipeline_name, r.workspace_id, r.number, r.state, r.params::text AS params, r.trigger, r.error, r.created_by, r.created_at, r.finished_at FROM pipeline.runs r JOIN pipeline.pipelines p ON p.id = r.pipeline_id WHERE r.id = @id AND r.workspace_id = @workspace_id;
