-- mlp:query GetVersionDevice :scalar
-- The device an optimized version was built and checked for (cpu | cuda); null for every other version.
SELECT v.optimization->>'device' FROM registry.versions v JOIN registry.models m ON m.id = v.model_id WHERE v.id = @id AND m.workspace_id = @workspace_id;
