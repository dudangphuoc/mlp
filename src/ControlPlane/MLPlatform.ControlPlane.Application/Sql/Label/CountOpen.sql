-- mlp:query CountOpen :scalar
SELECT count(*) FROM label.item_states WHERE project_id = @project_id AND annotator_id = @user_id AND status = 'in_progress' AND lock_owner = @user_id AND lock_expires_at > @now;
