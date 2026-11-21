-- Dynamic instance configuration (email provider, Telegram, …). Plain fields live in config (jsonb); secret fields are AES-GCM encrypted in secrets_enc.
CREATE TABLE IF NOT EXISTS platform.settings (
    key         text PRIMARY KEY,
    enabled     boolean     NOT NULL DEFAULT false,
    config      jsonb       NOT NULL DEFAULT '{}',
    secrets_enc text,
    updated_by  uuid,
    updated_at  timestamptz NOT NULL
);
