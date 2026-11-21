-- mlp:query FinishTrial :exec
-- Only a trial still waiting or running is finished (a cancelled one keeps its state).
UPDATE track.trials SET state = @state, stages = @stages_json::jsonb, steps_per_s = @steps_per_s, peak_vram_mb = @peak_vram_mb,
       vram_estimated = @vram_estimated, val_s_per_batch = @val_s_per_batch, train_images = @train_images, val_images = @val_images,
       error = @error, host_id = COALESCE(@host_id::uuid, host_id), finished_at = @now
WHERE id = @id AND workspace_id = @workspace_id AND state IN ('queued', 'running');
