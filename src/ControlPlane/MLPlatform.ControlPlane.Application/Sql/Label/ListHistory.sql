-- mlp:query ListHistory :many
SELECT h.action, u.username, h.at FROM label.annotation_history h JOIN identity.users u ON u.id = h.actor_id
WHERE h.dataset_id = @dataset_id AND h.path = @path ORDER BY h.at, h.id;
