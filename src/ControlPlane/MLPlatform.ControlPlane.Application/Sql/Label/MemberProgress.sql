-- mlp:query MemberProgress :many
SELECT annotator_id AS user_id,
       count(*) FILTER (WHERE status IN ('in_review', 'accepted')) AS submitted,
       count(*) FILTER (WHERE status = 'accepted') AS accepted,
       count(*) FILTER (WHERE status = 'rejected') AS returned,
       max(submitted_at) AS last_at
FROM label.item_states WHERE project_id = @project_id AND annotator_id IS NOT NULL GROUP BY annotator_id;
