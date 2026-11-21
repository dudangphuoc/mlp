-- mlp:query GetVersionPrefix :scalar
SELECT v.prefix FROM registry.versions v JOIN registry.models m ON m.id = v.model_id WHERE v.id = @id AND m.workspace_id = @workspace_id;
