-- mlp:query UpsertSettings :exec
INSERT INTO llmops.settings (deployment_id, workspace_id, pii_mode, block_injection, blocked_terms, check_output, price_per_1k, updated_by, updated_at)
VALUES (@deployment_id, @workspace_id, @pii_mode, @block_injection, @blocked_terms::jsonb, @check_output, @price_per1k, @updated_by, @now)
ON CONFLICT (deployment_id) DO UPDATE SET pii_mode = excluded.pii_mode, block_injection = excluded.block_injection,
  blocked_terms = excluded.blocked_terms, check_output = excluded.check_output, price_per_1k = excluded.price_per_1k,
  updated_by = excluded.updated_by, updated_at = excluded.updated_at;
