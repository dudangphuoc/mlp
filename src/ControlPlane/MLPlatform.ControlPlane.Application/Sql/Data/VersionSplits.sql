-- mlp:query VersionSplits :many
-- mlp:no-workspace the caller has already loaded these versions within the workspace
SELECT id, split_state, split_counts::text AS split_counts FROM data.dataset_versions WHERE id = ANY(@ids);
