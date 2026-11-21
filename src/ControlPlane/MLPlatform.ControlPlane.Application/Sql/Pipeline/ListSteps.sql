-- mlp:query ListSteps :many
SELECT step_id, kind, state, attempt, ref_id, output::text AS output, error, started_at, finished_at
FROM pipeline.steps WHERE run_id = @run_id ORDER BY started_at NULLS LAST, step_id;
