-- mlp:query MarkLabelsImported :exec
-- mlp:no-workspace the caller has loaded the project within the workspace
UPDATE label.item_states SET labels_imported = true WHERE project_id = @project_id AND path = @path;
