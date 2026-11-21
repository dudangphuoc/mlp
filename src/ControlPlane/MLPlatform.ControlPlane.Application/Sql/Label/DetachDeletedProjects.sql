-- mlp:query DetachDeletedProjects :exec
-- Deleted projects keep their rows (labels and history stay); they let go of the version so it can be deleted.
UPDATE label.projects SET version_id = NULL WHERE version_id = @version_id AND workspace_id = @workspace_id AND state = 'deleted';
