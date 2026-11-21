-- mlp:query ListAudit :many
-- mlp:no-workspace AI providers are instance-wide (Instance Admin)
SELECT a.id, a.actor_id, coalesce(u.display_name, u.username, '') AS actor_name, a.action, a.target, a.before::text AS before_json, a.after::text AS after_json, a.at
FROM ai.audit a LEFT JOIN identity.users u ON u.id = a.actor_id ORDER BY a.at DESC LIMIT @lim;
