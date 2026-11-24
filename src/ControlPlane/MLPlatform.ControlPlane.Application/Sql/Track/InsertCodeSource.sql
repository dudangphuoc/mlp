-- mlp:query InsertCodeSource :exec
INSERT INTO track.code_sources (id, workspace_id, url_prefix, secret_enc, created_by, created_at)
VALUES (@id, @workspace_id, @url_prefix, @secret_enc, @created_by, @now);
