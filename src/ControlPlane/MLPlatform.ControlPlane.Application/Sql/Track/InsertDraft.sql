-- mlp:query InsertDraft :exec
INSERT INTO track.drafts (id, workspace_id, project_recipe_id, user_id, base_revision_id, variant, params, dataset_version_id, start_kind, train_kind,
                          retrain_of_run_id, host_id, gpus, cpus, run_name, etag, updated_at)
VALUES (@id, @workspace_id, @project_recipe_id, @user_id, @base_revision_id, @variant, @params_json::jsonb, @dataset_version_id, @start_kind, @train_kind,
        @retrain_of_run_id, @host_id, @gpus, @cpus, @run_name, 0, @now)
ON CONFLICT (project_recipe_id, user_id) DO NOTHING;
