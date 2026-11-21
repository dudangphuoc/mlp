-- mlp:query SetProjectState :exec
UPDATE label.projects SET state = @state WHERE id = @id;
