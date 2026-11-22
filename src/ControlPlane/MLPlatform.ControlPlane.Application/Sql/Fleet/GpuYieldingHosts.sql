-- mlp:query GpuYieldingHosts :many
SELECT id FROM fleet.hosts WHERE workspace_id = @workspace_id AND gpu_yield;
