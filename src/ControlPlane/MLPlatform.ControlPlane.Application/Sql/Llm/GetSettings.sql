-- mlp:query GetSettings :one
SELECT deployment_id, pii_mode, block_injection, blocked_terms::text AS blocked_terms, check_output, price_per_1k AS price_per1k, updated_at
FROM llmops.settings WHERE deployment_id = @deployment_id;
