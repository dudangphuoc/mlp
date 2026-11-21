-- mlp:query ListRunArtifacts :many
SELECT path, size_bytes FROM track.run_artifacts WHERE run_id = @run_id ORDER BY path;
