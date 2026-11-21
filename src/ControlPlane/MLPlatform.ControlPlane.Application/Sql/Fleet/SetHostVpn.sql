-- mlp:query SetHostVpn :exec
-- mlp:no-workspace agent session only knows the host id
UPDATE fleet.hosts SET vpn_status = @status, vpn_message = @message, vpn_checked_at = @now WHERE id = @id;
