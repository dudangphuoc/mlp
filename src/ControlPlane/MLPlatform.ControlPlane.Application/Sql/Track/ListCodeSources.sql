-- mlp:query ListCodeSources :many
SELECT id, workspace_id, url_prefix, secret_enc, created_at FROM track.code_sources WHERE workspace_id = @workspace_id ORDER BY url_prefix;
