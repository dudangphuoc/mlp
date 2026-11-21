-- mlp:query InsertEvent :exec
INSERT INTO ops.events (id, at, received_at, workspace_id, source, resource_type, resource_id, kind, severity, message, data)
VALUES (@id, @at, @now, @workspace_id, @source, @resource_type, @resource_id, @kind, @severity, @message, @data::jsonb);
