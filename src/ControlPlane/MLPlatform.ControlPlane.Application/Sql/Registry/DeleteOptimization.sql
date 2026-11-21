-- mlp:query DeleteOptimization :exec
DELETE FROM registry.optimizations WHERE workspace_id = @workspace_id AND id = @id;
