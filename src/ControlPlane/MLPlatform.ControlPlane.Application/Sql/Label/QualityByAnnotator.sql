-- mlp:query QualityByAnnotator :many
-- One row per annotator: what they submitted, what reviewers and Hermes concluded (latest automatic check per item).
WITH p AS (SELECT id, dataset_id FROM label.projects WHERE id = @project_id AND workspace_id = @workspace_id),
latest AS (
  SELECT DISTINCT ON (c.path) c.path, c.verdict, c.confidence, c.action
  FROM label.hermes_checks c JOIN p ON p.id = c.project_id WHERE c.trigger = 'submit' ORDER BY c.path, c.created_at DESC),
returned AS (
  SELECT DISTINCT h.path FROM label.annotation_history h JOIN p ON p.dataset_id = h.dataset_id WHERE h.action IN ('reject', 'reopen', 'hermes.undo'))
SELECT s.annotator_id, coalesce(nullif(u.display_name, ''), u.username) AS annotator_name,
  count(*) AS items,
  count(*) FILTER (WHERE s.status IN ('in_review', 'accepted', 'rejected')) AS submitted,
  count(*) FILTER (WHERE s.status = 'accepted') AS accepted,
  count(*) FILTER (WHERE s.status = 'rejected') AS rejected,
  count(*) FILTER (WHERE s.status = 'in_review') AS in_review,
  count(r.path) AS returned_ever,
  count(l.path) AS checked,
  count(*) FILTER (WHERE l.verdict = 'pass') AS hermes_pass,
  count(*) FILTER (WHERE l.verdict = 'flag') AS hermes_flag,
  count(*) FILTER (WHERE l.verdict = 'fail') AS hermes_fail,
  count(*) FILTER (WHERE l.action = 'auto_accepted') AS auto_accepted,
  count(*) FILTER (WHERE l.action = 'undone') AS undone,
  avg(l.confidence) AS avg_confidence
FROM label.item_states s JOIN p ON p.id = s.project_id
LEFT JOIN identity.users u ON u.id = s.annotator_id
LEFT JOIN latest l ON l.path = s.path
LEFT JOIN returned r ON r.path = s.path
WHERE s.annotator_id IS NOT NULL
GROUP BY s.annotator_id, u.display_name, u.username ORDER BY submitted DESC;
