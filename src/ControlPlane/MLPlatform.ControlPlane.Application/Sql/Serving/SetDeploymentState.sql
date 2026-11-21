-- mlp:query SetDeploymentState :exec
UPDATE serving.deployments SET state = @state, state_reason = @reason, endpoint = coalesce(@endpoint, endpoint) WHERE id = @id;
