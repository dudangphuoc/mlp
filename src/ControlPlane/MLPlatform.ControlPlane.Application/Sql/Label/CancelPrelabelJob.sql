-- mlp:query CancelPrelabelJob :exec
UPDATE label.prelabel_jobs SET state = 'cancelled', finished_at = @now WHERE id = @id AND workspace_id = @workspace_id AND state IN ('queued', 'running');
