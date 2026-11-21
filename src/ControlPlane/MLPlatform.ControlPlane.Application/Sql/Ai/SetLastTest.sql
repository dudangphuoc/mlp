-- mlp:query SetLastTest :exec
-- mlp:no-workspace AI providers are instance-wide (Instance Admin)
UPDATE ai.models SET last_test_run_id = @run_id WHERE id = @id;
