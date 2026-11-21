-- mlp:query DraftsUsingVersion :scalar
SELECT count(*) FROM track.drafts WHERE dataset_version_id = @version_id AND workspace_id = @workspace_id;
