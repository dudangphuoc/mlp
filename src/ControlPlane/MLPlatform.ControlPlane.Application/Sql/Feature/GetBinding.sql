-- mlp:query GetBinding :one
SELECT b.deployment_id, b.workspace_id, b.entity_id, e.name AS entity_name, e.join_key, b.features::text AS features, b.updated_at
FROM feature.bindings b JOIN feature.entities e ON e.id = b.entity_id WHERE b.deployment_id = @deployment_id;
