-- mlp:query MarkHostsWaiting :exec
-- No agent channel exists right after the control plane starts: hosts show as offline until their agent reconnects (seconds).
UPDATE fleet.hosts SET state = 'offline', state_reason = 'Waiting for the agent to reconnect after a control-plane restart'
WHERE state NOT IN ('offline', 'enrolling');
