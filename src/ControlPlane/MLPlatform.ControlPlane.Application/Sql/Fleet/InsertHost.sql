-- mlp:query InsertHost :exec
INSERT INTO fleet.hosts (id, workspace_id, name, state, overlay_ip, wg_public_key, credential_hash, agent_version,
                         data_path, models_path, host_info, created_at, last_seen_at)
VALUES (@id, @workspace_id, @name, 'enrolling', @overlay_ip::inet, @wg_public_key, @credential_hash, @agent_version,
        @data_path, @models_path, @host_info, @now, @now);
