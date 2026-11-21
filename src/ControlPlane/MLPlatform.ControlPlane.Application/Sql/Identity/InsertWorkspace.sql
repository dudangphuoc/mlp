-- mlp:query InsertWorkspace :exec
INSERT INTO identity.workspaces (id, slug, name, kind, owner_id, created_at)
VALUES (@id, @slug, @name, @kind, @owner_id, @created_at);
