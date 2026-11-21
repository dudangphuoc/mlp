-- mlp:query ListComments :many
SELECT c.id, u.username AS author, c.body, c.resolved, c.created_at FROM label.comments c JOIN identity.users u ON u.id = c.author_id
WHERE c.project_id = @project_id AND c.path = @path ORDER BY c.created_at;
