-- mlp:query CompleteBootstrap :exec
UPDATE identity.instance_settings
SET name = @name, allow_signup = @allow_signup, bootstrapped_at = @now, bootstrap_token_hash = NULL
WHERE id = 1 AND bootstrapped_at IS NULL;
