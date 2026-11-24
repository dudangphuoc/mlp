-- mlp:query RunExtras :many
-- What a run cooked: its revision number and how it started. Runs from before revisions (or from pipelines / sweeps) have no revision.
-- Code runs (SDK) carry where they ran, the code they came from and how the process ended.
SELECT r.id AS run_id, r.revision_id, v.number AS revision_number, r.train_kind, r.start_kind, o.number AS retrain_of_number, r.host_id,
       r.origin, r.host_name, r.code::text AS code, r.exit_code, r.heartbeat_at
FROM track.runs r
LEFT JOIN track.revisions v ON v.id = r.revision_id AND v.workspace_id = r.workspace_id
LEFT JOIN track.runs o ON o.id = r.retrain_of_run_id AND o.workspace_id = r.workspace_id
WHERE r.workspace_id = @workspace_id AND r.id = ANY(@ids);
