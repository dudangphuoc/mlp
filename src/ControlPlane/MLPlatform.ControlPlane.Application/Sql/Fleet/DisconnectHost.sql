-- mlp:query DisconnectHost :exec
UPDATE fleet.hosts SET state = 'disconnected', disconnected_at = @now, credential_hash = 'revoked:' || id::text
WHERE id = @id AND workspace_id = @workspace_id;
