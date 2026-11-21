-- mlp:query ReEnrollHost :exec
UPDATE fleet.hosts SET credential_hash = @credential_hash, wg_public_key = @wg_public_key, agent_version = @agent_version,
       host_info = @host_info, data_path = @data_path, models_path = @models_path, state = 'enrolling',
       disconnected_at = NULL, last_seen_at = @now
WHERE id = @id;
