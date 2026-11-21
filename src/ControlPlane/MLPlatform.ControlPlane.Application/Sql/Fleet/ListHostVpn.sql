-- mlp:query ListHostVpn :many
SELECT id, vpn_status, vpn_message, vpn_checked_at FROM fleet.hosts WHERE workspace_id = @workspace_id AND disconnected_at IS NULL;
