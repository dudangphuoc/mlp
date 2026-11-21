-- mlp:query VersionNames :many
SELECT v.id, v.dataset_id, d.name AS dataset_name, v.number
FROM data.dataset_versions v JOIN data.datasets d ON d.id = v.dataset_id
WHERE d.workspace_id = @workspace_id AND v.id = ANY(@ids);
