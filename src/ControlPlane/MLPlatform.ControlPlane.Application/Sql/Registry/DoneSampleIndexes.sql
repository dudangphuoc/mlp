-- mlp:query DoneSampleIndexes :many
SELECT idx FROM registry.evaluation_samples WHERE evaluation_id = @evaluation_id;
