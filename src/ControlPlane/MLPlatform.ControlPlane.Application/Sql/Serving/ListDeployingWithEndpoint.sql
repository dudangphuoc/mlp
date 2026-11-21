-- mlp:query ListDeployingWithEndpoint :many
SELECT id, workspace_id, slug, model_version_id, model_name, model_version, task, state, state_reason, job_id, endpoint, created_at, purpose, idle_stop_min, last_used_at, cpus, gpus, image_override
FROM serving.deployments d
WHERE d.endpoint IS NOT NULL AND (d.state IN ('deploying','healthy','degraded')
   -- self-heal: marked failed while its replica job is in fact still running → probe again, a healthy answer brings it back
   OR (d.state = 'failed' AND EXISTS (SELECT 1 FROM sched.jobs j WHERE j.id = d.job_id AND j.state = 'running')));
