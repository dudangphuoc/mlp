-- mlp:query HostInfoForNotify :one
SELECT id, workspace_id, name, state FROM fleet.hosts WHERE id = @id;
