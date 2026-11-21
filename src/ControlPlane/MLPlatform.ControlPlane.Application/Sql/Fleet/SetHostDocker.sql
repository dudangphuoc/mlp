-- mlp:query SetHostDocker :scalar
-- mlp:no-workspace agent session only knows the host id. Returns true when the Docker state changes (so an event is recorded).
WITH old AS (SELECT docker_ok FROM fleet.hosts WHERE id = @id)
UPDATE fleet.hosts h SET docker_ok = @ok, docker_error = CASE WHEN @ok THEN NULL ELSE @error END,
    state = CASE WHEN @ok AND h.state = 'degraded' THEN 'online'
                 WHEN NOT @ok AND h.state IN ('online', 'busy') THEN 'degraded' ELSE h.state END,
    state_reason = CASE WHEN @ok AND h.state = 'degraded' THEN NULL
                        WHEN NOT @ok AND h.state IN ('online', 'busy', 'degraded') THEN 'Docker is not running on this machine: ' || @error ELSE h.state_reason END
FROM old WHERE h.id = @id
RETURNING old.docker_ok IS DISTINCT FROM @ok;
