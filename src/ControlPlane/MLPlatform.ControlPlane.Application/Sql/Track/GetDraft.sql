-- mlp:query GetDraft :one
SELECT id, project_recipe_id, user_id, base_revision_id, variant, params::text AS params, dataset_version_id, start_kind, train_kind, retrain_of_run_id,
       host_id, gpus, cpus, run_name, etag
FROM track.drafts WHERE project_recipe_id = @project_recipe_id AND user_id = @user_id AND workspace_id = @workspace_id;
