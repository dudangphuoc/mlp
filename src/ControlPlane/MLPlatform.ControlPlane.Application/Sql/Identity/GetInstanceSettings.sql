-- mlp:query GetInstanceSettings :one
SELECT name, allow_signup, bootstrap_token_hash, bootstrap_expires_at, bootstrapped_at
FROM identity.instance_settings WHERE id = 1;
