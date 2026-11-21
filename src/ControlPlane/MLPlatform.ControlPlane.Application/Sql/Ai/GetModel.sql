-- mlp:query GetModel :one
-- mlp:no-workspace AI providers are instance-wide (Instance Admin)
SELECT id, connection_id, model_id, display_name, roles, inputs, outputs, skills, tier, input_usd_per_1m AS input_usd_per1m, output_usd_per_1m AS output_usd_per1m, max_tokens, is_active, health, health_reason, health_at, consecutive_failures, last_test_run_id, created_at, updated_at FROM ai.models WHERE id = @id;
