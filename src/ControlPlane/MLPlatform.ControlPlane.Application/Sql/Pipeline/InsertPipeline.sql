-- mlp:query InsertPipeline :exec
INSERT INTO pipeline.pipelines (id, workspace_id, name, definition, trigger, created_by, created_at, updated_at)
VALUES (@id, @workspace_id, @name, @definition::jsonb, @trigger::jsonb, @created_by, @now, @now);
