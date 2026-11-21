-- mlp:query RemoveMember :exec
DELETE FROM label.project_members WHERE project_id = @project_id AND user_id = @user_id AND role = @role;
