-- mlp:query ProjectSummaries :many
-- Progress of every labeling project in the workspace, plus what the caller has on their plate.
SELECT p.id AS project_id,
       count(s.path) AS total,
       count(*) FILTER (WHERE s.status = 'accepted') AS accepted,
       count(*) FILTER (WHERE s.status = 'in_review') AS in_review,
       count(*) FILTER (WHERE s.status = 'unlabeled') AS unlabeled,
       count(*) FILTER (WHERE s.status IN ('in_progress', 'rejected') AND s.annotator_id = @user_id) AS my_open,
       count(*) FILTER (WHERE s.status = 'rejected' AND s.annotator_id = @user_id) AS my_returned,
       (SELECT coalesce(string_agg(m.role, ',' ORDER BY m.role), '') FROM label.project_members m
         WHERE m.project_id = p.id AND m.user_id = @user_id) AS my_roles
FROM label.projects p LEFT JOIN label.item_states s ON s.project_id = p.id
WHERE p.workspace_id = @workspace_id AND p.state <> 'deleted'
GROUP BY p.id;
