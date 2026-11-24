-- mlp:query MarkDebugRunCancelled :exec
-- The developer ended the debug session: a run that just failed because its debugger was stopped counts as cancelled.
UPDATE track.runs SET state = 'cancelled'
WHERE id = @id AND workspace_id = @workspace_id AND origin = 'code' AND state = 'failed' AND ended_at > @since;
