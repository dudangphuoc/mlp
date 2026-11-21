-- mlp:query MergeRecent :scalar
-- R1: same user, same kind, same resource, unread, within 5 minutes → increment count instead of creating a new row.
WITH m AS (
  UPDATE notify.notifications SET count = count + 1, title = @title, updated_at = @now
  WHERE id = (SELECT id FROM notify.notifications
              WHERE user_id = @user_id AND workspace_id = @workspace_id AND kind = @kind AND resource_uri = @resource_uri
                AND read_at IS NULL AND created_at > @now - interval '5 minutes'
              ORDER BY created_at DESC LIMIT 1 FOR UPDATE)
  RETURNING 1)
SELECT count(*) FROM m;
