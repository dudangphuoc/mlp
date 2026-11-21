-- mlp:query CalibrationRows :many
-- Each automatic check with the first human decision that followed it (accept / reject / undo of an auto-accept / reopen), if any.
SELECT c.verdict, c.confidence, c.action,
  coalesce((SELECT h.action FROM label.annotation_history h
            WHERE h.dataset_id = p.dataset_id AND h.path = c.path AND h.at > c.created_at AND h.action IN ('accept', 'reject', 'hermes.undo', 'reopen')
            ORDER BY h.at LIMIT 1), '') AS outcome
FROM label.hermes_checks c JOIN label.projects p ON p.id = c.project_id
WHERE c.project_id = @project_id AND c.workspace_id = @workspace_id AND c.trigger = 'submit' AND c.verdict IN ('pass', 'flag', 'fail');
