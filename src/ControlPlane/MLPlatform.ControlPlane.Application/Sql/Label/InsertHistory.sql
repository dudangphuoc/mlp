-- mlp:query InsertHistory :exec
INSERT INTO label.annotation_history (id, dataset_id, path, action, before, after, actor_id, at)
VALUES (@id, @dataset_id, @path, @action, @before::jsonb, @after::jsonb, @actor_id, @now);
