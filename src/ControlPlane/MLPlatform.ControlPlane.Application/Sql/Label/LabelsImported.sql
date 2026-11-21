-- mlp:query LabelsImported :scalar
-- mlp:no-workspace the caller has loaded the project within the workspace
SELECT labels_imported FROM label.item_states WHERE project_id = @project_id AND path = @path;
