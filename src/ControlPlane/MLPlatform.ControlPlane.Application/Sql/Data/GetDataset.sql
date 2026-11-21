-- mlp:query GetDataset :one
SELECT d.id, d.workspace_id, d.slug, d.name, d.task, d.status, d.created_at,
       -- files of the latest version plus files added since (the pending change of the next version)
       coalesce((SELECT v.file_count FROM data.dataset_versions v WHERE v.dataset_id = d.id ORDER BY v.number DESC LIMIT 1), 0)
         + (SELECT count(*) FROM data.files f WHERE f.dataset_id = d.id) AS file_count,
       (coalesce((SELECT v.size_bytes FROM data.dataset_versions v WHERE v.dataset_id = d.id ORDER BY v.number DESC LIMIT 1), 0)
         + (SELECT coalesce(sum(f.size_bytes), 0) FROM data.files f WHERE f.dataset_id = d.id))::bigint AS size_bytes,
       (SELECT coalesce(max(v.number), 0) FROM data.dataset_versions v WHERE v.dataset_id = d.id) AS current_version,
       (SELECT count(*) FROM data.files f WHERE f.dataset_id = d.id) AS pending_files,
       (SELECT count(*) FROM data.pending_removals r WHERE r.dataset_id = d.id) AS pending_removals
FROM data.datasets d WHERE d.id = @id AND d.workspace_id = @workspace_id;
