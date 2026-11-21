-- mlp:query NextPrelabelPaths :many
-- mlp:no-workspace background runner; the job row (already workspace-scoped) names the project
SELECT i.path FROM label.item_states i
WHERE i.project_id = @project_id AND i.status IN ('unlabeled', 'in_progress', 'rejected')
  AND NOT EXISTS (SELECT 1 FROM label.annotations a WHERE a.dataset_id = i.dataset_id AND a.path = i.path AND a.deleted_at IS NULL)
  AND NOT EXISTS (SELECT 1 FROM label.suggestion_sets s WHERE s.project_id = i.project_id AND s.path = i.path AND (s.job_id = @job_id OR (s.decision = 'pending' AND s.state = 'proposed')))
ORDER BY i.priority_score DESC, i.path LIMIT @lim;
