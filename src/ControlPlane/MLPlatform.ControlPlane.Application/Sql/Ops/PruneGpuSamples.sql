-- mlp:query PruneGpuSamples :exec
WITH raw AS (DELETE FROM ops.gpu_samples WHERE at < @raw_before)
DELETE FROM ops.gpu_samples_1m WHERE at < @rollup_before;
