-- Settings (S-01-2..5): an API key is a long-lived session (kind = api_key, named, token mlpk_…) — Bearer auth shares the session path.
ALTER TABLE identity.sessions ADD COLUMN IF NOT EXISTS kind text NOT NULL DEFAULT 'session';
ALTER TABLE identity.sessions ADD COLUMN IF NOT EXISTS name text;
CREATE INDEX IF NOT EXISTS sessions_user_kind ON identity.sessions (user_id, kind) WHERE revoked_at IS NULL;
ALTER TABLE identity.invitations ADD COLUMN IF NOT EXISTS revoked_at timestamptz;
