-- mlp:query LatestVersion :one
SELECT v.id, v.dataset_id, v.number, v.prefix, v.file_count, v.size_bytes, v.note, v.created_at, v.storage, v.source_ref
FROM data.dataset_versions v JOIN data.datasets d ON d.id = v.dataset_id
WHERE v.dataset_id = @dataset_id AND d.workspace_id = @workspace_id ORDER BY v.number DESC LIMIT 1;
