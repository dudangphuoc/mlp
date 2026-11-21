-- mlp:query CountPrelabelTargets :scalar
-- Items nobody labeled yet and without a pending suggestion: the scope of a batch pre-label.
SELECT count(*) FROM label.item_states i JOIN label.projects p ON p.id = i.project_id
WHERE i.project_id = @project_id AND p.workspace_id = @workspace_id AND i.status IN ('unlabeled', 'in_progress', 'rejected')
  AND NOT EXISTS (SELECT 1 FROM label.annotations a WHERE a.dataset_id = i.dataset_id AND a.path = i.path AND a.deleted_at IS NULL)
  AND NOT EXISTS (SELECT 1 FROM label.suggestion_sets s WHERE s.project_id = i.project_id AND s.path = i.path AND s.decision = 'pending' AND s.state = 'proposed');
