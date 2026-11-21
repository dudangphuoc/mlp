-- mlp:query UpdateModel :exec
-- mlp:no-workspace AI providers are instance-wide (Instance Admin)
UPDATE ai.models SET display_name = @display_name, roles = @roles, inputs = @inputs, outputs = @outputs, skills = @skills, tier = @tier,
    input_usd_per_1m = @input_usd_per1m, output_usd_per_1m = @output_usd_per1m, max_tokens = @max_tokens, is_active = @is_active, updated_at = @now WHERE id = @id;
