-- mlp:query HermesStats :many
SELECT verdict, action, count(*) AS n, avg(confidence) AS avg_confidence
FROM label.hermes_checks WHERE workspace_id = @workspace_id AND project_id = @project_id GROUP BY verdict, action;
