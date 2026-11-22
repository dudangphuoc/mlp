-- mlp:query HostAlertTarget :one
-- mlp:no-workspace internal lookup for alert routing
SELECT workspace_id, name FROM fleet.hosts WHERE id = @id;
