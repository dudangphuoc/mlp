-- mlp:query DeleteWorkingFile :scalar
-- mlp:no-workspace the caller has loaded the dataset within the workspace
WITH d AS (DELETE FROM data.files WHERE dataset_id = @dataset_id AND path = @path RETURNING 1) SELECT count(*) FROM d;
