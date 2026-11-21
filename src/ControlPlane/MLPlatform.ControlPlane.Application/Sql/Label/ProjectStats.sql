-- mlp:query ProjectStats :many
SELECT status, count(*) AS n FROM label.item_states WHERE project_id = @project_id GROUP BY status ORDER BY status;
