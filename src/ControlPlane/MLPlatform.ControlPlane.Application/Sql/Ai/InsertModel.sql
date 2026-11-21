-- mlp:query InsertModel :exec
-- mlp:no-workspace AI providers are instance-wide (Instance Admin)
INSERT INTO ai.models (id, connection_id, model_id, display_name, roles, inputs, outputs, skills, tier, input_usd_per_1m, output_usd_per_1m, max_tokens, is_active, created_at, updated_at)
VALUES (@id, @connection_id, @model_id, @display_name, @roles, @inputs, @outputs, @skills, @tier, @input_usd_per1m, @output_usd_per1m, @max_tokens, true, @now, @now);
