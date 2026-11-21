-- mlp:query SetVersionReference :exec
-- mlp:no-workspace the import worker updates a version it reserved in the workspace
UPDATE registry.versions SET storage = 'reference' WHERE id = @id;
