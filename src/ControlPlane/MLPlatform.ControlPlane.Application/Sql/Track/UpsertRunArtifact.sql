-- mlp:query UpsertRunArtifact :exec
INSERT INTO track.run_artifacts (run_id, path, size_bytes) VALUES (@run_id, @path, @size_bytes)
ON CONFLICT (run_id, path) DO UPDATE SET size_bytes = excluded.size_bytes;
