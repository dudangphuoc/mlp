-- mlp:query DeleteExport :exec
DELETE FROM registry.exports WHERE workspace_id = @workspace_id AND id = @id;
