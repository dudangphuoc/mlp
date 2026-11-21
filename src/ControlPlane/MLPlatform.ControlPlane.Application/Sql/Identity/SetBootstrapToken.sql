-- mlp:query SetBootstrapToken :exec
UPDATE identity.instance_settings SET bootstrap_token_hash = @token_hash, bootstrap_expires_at = @expires_at
WHERE id = 1 AND bootstrapped_at IS NULL;
