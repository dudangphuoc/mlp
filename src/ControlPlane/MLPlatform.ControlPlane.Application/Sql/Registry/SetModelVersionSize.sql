-- mlp:query SetModelVersionSize :exec
-- mlp:no-workspace HF import worker updates the version it just created by id
UPDATE registry.versions SET file_count = @file_count, size_bytes = @size_bytes WHERE id = @id;
