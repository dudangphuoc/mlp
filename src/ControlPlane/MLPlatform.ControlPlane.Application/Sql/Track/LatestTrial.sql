-- mlp:query LatestTrial :one
-- Newest trial of the same content (trial key) on the same data and start; a pinned machine must match too. passed_only → the one that opens the gate.
SELECT t.id, t.workspace_id, t.project_id, t.project_recipe_id, t.revision_id, t.run_id, t.trial_key, t.dataset_version_id, t.start_kind, t.host_id, t.gpus,
       t.state, t.stages::text AS stages, t.steps_per_s, t.peak_vram_mb, t.vram_estimated, t.val_s_per_batch, t.train_images, t.val_images, t.error,
       t.created_by, t.created_at, t.finished_at, v.number AS revision_number, r.job_id
FROM track.trials t
JOIN track.revisions v ON v.id = t.revision_id AND v.workspace_id = t.workspace_id
LEFT JOIN track.runs r ON r.id = t.run_id AND r.workspace_id = t.workspace_id
WHERE t.project_recipe_id = @project_recipe_id AND t.workspace_id = @workspace_id AND t.trial_key = @trial_key
  AND t.dataset_version_id IS NOT DISTINCT FROM @dataset_version_id::uuid AND t.start_kind = @start_kind
  AND (@host_id::uuid IS NULL OR t.host_id = @host_id) AND (NOT @passed_only OR t.state = 'passed')
ORDER BY t.created_at DESC LIMIT 1;
