-- mlp:query InsertTrial :exec
INSERT INTO track.trials (id, workspace_id, project_id, project_recipe_id, revision_id, run_id, trial_key, dataset_version_id, start_kind, host_id, gpus,
                          state, train_images, val_images, created_by, created_at)
VALUES (@id, @workspace_id, @project_id, @project_recipe_id, @revision_id, @run_id, @trial_key, @dataset_version_id, @start_kind, @host_id, @gpus,
        'queued', @train_images, @val_images, @created_by, @now);
