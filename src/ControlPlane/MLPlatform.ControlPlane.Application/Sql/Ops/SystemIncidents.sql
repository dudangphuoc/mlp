-- mlp:query SystemIncidents :many
SELECT id, at, source, resource_type, resource_id, kind, severity, message, data::text AS data
FROM ops.events WHERE resource_type = 'system' AND at > @since ORDER BY at DESC LIMIT 100;
