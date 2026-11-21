-- mlp:query ListTrials :many
SELECT t.number, t.params::text AS params, t.run_id, t.state, t.value, t.error, t.created_at, r.job_id
FROM tune.trials t LEFT JOIN track.runs r ON r.id = t.run_id
WHERE t.sweep_id = @sweep_id ORDER BY t.number;
