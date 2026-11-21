-- mlp:query SetOptimizationPromoted :exec
UPDATE registry.optimizations SET promoted = promoted || jsonb_build_object(@candidate::text, @version_id::text)
WHERE workspace_id = @workspace_id AND id = @id;
