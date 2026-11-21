-- mlp:query MemberRoles :many
SELECT role FROM label.project_members WHERE project_id = @project_id AND user_id = @user_id;
