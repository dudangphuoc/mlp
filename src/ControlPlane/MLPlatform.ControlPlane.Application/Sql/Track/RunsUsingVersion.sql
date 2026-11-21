-- mlp:query RunsUsingVersion :scalar
SELECT count(*) FROM track.runs WHERE dataset_version_id = @version_id AND workspace_id = @workspace_id;
