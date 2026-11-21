-- mlp:query InsertSweep :exec
INSERT INTO tune.sweeps (id, workspace_id, project_id, name, recipe, variant, task, algorithm, space, base_params, objective, direction,
  max_trials, parallel, seed, cpus, gpus, dataset_version_id, image_override, network, state, created_by, created_at)
VALUES (@id, @workspace_id, @project_id, @name, @recipe, @variant, @task, @algorithm, @space::jsonb, @base_params::jsonb, @objective, @direction,
  @max_trials, @parallel, @seed, @cpus, @gpus, @dataset_version_id, @image_override, @network, 'running', @created_by, @now);
