-- mlp:query InsertEvent :exec
INSERT INTO audit.events (id, at, workspace_id, actor_id, action, outcome, reason, prev_hash, hash)
VALUES (@id, @at, @workspace_id, @actor_id, @action, @outcome, @reason, @prev_hash, @hash);
