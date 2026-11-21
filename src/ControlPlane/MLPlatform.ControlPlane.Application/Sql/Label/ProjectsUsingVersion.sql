-- mlp:query ProjectsUsingVersion :scalar
SELECT count(*) FROM label.projects WHERE version_id = @version_id AND workspace_id = @workspace_id AND state <> 'deleted';
