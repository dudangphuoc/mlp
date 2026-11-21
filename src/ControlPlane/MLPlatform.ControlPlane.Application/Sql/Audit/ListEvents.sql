-- mlp:query ListEvents :many
SELECT seq, id, at, workspace_id, actor_id, action, outcome, reason, prev_hash, hash
FROM audit.events WHERE workspace_id = @workspace_id ORDER BY seq DESC LIMIT 500;
