-- mlp:query ArchiveOtherInStage :many
UPDATE registry.versions SET stage = 'archived' WHERE model_id = @model_id AND stage = @stage AND id <> @keep_id RETURNING id;
