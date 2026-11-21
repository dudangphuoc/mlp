-- mlp:query ClearOffline :exec
DELETE FROM feature.offline WHERE view_id = @view_id;
