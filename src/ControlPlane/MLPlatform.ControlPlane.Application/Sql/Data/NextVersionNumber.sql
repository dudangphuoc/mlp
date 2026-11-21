-- mlp:query NextVersionNumber :scalar
-- Monotonic per dataset (version_seq): a deleted version's number is never handed out again.
UPDATE data.datasets d
SET version_seq = greatest(d.version_seq, coalesce((SELECT max(number) FROM data.dataset_versions v WHERE v.dataset_id = d.id), 0)) + 1
WHERE d.id = @dataset_id AND d.workspace_id = @workspace_id
RETURNING d.version_seq;
