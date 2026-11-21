-- mlp:query GetTrialByRun :one
SELECT t.id, t.workspace_id, t.project_id, t.project_recipe_id, t.revision_id, t.run_id, t.trial_key, t.dataset_version_id, t.start_kind, t.host_id, t.gpus,
       t.state, t.stages::text AS stages, t.steps_per_s, t.peak_vram_mb, t.vram_estimated, t.val_s_per_batch, t.train_images, t.val_images, t.error,
       t.created_by, t.created_at, t.finished_at, v.number AS revision_number, r.job_id
FROM track.trials t
JOIN track.revisions v ON v.id = t.revision_id AND v.workspace_id = t.workspace_id
LEFT JOIN track.runs r ON r.id = t.run_id AND r.workspace_id = t.workspace_id
WHERE t.run_id = @run_id AND t.workspace_id = @workspace_id;
