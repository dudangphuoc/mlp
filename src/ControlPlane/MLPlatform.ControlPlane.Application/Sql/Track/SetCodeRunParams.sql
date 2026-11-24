-- mlp:query SetCodeRunParams :exec
UPDATE track.runs SET params = params || @params_json::jsonb WHERE id = @id AND workspace_id = @workspace_id AND origin = 'code';
