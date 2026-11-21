-- mlp:query InsertOptimization :exec
INSERT INTO registry.optimizations (id, workspace_id, source_version_id, export_id, device, candidates, host_id, calib_dataset_version_id,
    check_dataset_version_id, conf, state, job_id, created_by, created_at)
VALUES (@id, @workspace_id, @source_version_id, @export_id, @device, string_to_array(@candidates, ','), @host_id, @calib_dataset_version_id,
    @check_dataset_version_id, @conf, 'running', @job_id, @created_by, @now);
