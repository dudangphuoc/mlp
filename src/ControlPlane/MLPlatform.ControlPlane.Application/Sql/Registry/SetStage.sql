-- mlp:query SetStage :exec
UPDATE registry.versions SET stage = @stage WHERE id = @id;
