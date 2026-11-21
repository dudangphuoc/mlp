-- mlp:query FinishRun :exec
-- mlp:no-workspace runs may be instance-level (workspace_id null)
UPDATE ai.runs SET state = @state, fallback = @fallback, model_ref = coalesce(@model_ref, model_ref), model_label = coalesce(@model_label, model_label),
    output = @output_json::jsonb, decided_by = @decided_by, confidence = @confidence, latency_ms = @latency_ms, tokens_in = @tokens_in, tokens_out = @tokens_out,
    cost_usd = @cost_usd, attempts = @attempts_json::jsonb, error_code = @error_code, error = @error, finished_at = @now WHERE id = @id;
