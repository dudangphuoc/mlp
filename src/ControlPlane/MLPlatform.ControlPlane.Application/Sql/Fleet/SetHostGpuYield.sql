-- mlp:query SetHostGpuYield :exec
-- mlp:no-workspace agent channel already identifies the host
UPDATE fleet.hosts SET gpu_yield = @yield WHERE id = @id AND gpu_yield <> @yield;
