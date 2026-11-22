-- mlp:query GpuHistoryRollup :many
SELECT to_timestamp(floor(extract(epoch FROM at) / @step) * @step) AS at, avg(util)::float8 AS util, max(vram_used) AS vram_used,
       max(vram_total) AS vram_total, max(temp)::float8 AS temp, avg(power)::float8 AS power, avg(sm_clock)::float8 AS sm_clock
FROM ops.gpu_samples_1m WHERE host_id = @host_id AND gpu_uuid = @gpu_uuid AND at >= @since
GROUP BY 1 ORDER BY 1;
