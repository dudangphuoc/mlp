-- An API key may be limited to one workspace (NULL = every workspace of its owner).
ALTER TABLE identity.sessions ADD COLUMN IF NOT EXISTS key_workspace_id uuid;
