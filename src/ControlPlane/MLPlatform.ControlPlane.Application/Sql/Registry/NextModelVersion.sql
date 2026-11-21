-- mlp:query NextModelVersion :scalar
SELECT coalesce(max(version), 0) + 1 FROM registry.versions WHERE model_id = @model_id;
