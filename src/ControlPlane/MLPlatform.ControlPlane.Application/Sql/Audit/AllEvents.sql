-- mlp:query AllEvents :many
SELECT seq, id, at, workspace_id, actor_id, action, outcome, reason, prev_hash, hash FROM audit.events ORDER BY seq;
