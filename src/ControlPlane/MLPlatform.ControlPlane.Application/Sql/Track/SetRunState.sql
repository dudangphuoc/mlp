-- mlp:query SetRunState :exec
UPDATE track.runs SET state = @state, ended_at = CASE WHEN @state IN ('succeeded','failed','cancelled') THEN @now ELSE ended_at END
WHERE id = @id;
