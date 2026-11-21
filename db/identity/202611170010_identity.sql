-- M01 §3
CREATE SCHEMA IF NOT EXISTS identity;

CREATE TABLE identity.users (
    id                uuid PRIMARY KEY,
    email             text        NOT NULL UNIQUE,
    email_verified_at timestamptz,
    username          text        NOT NULL UNIQUE,
    display_name      text        NOT NULL,
    password_hash     text        NOT NULL,
    status            text        NOT NULL DEFAULT 'active',
    is_instance_admin boolean     NOT NULL DEFAULT false,
    created_at        timestamptz NOT NULL,
    last_login_at     timestamptz
);

CREATE TABLE identity.sessions (
    id           uuid PRIMARY KEY,
    user_id      uuid        NOT NULL REFERENCES identity.users(id),
    token_hash   text        NOT NULL UNIQUE,
    created_at   timestamptz NOT NULL,
    last_seen_at timestamptz NOT NULL,
    revoked_at   timestamptz
);

CREATE TABLE identity.workspaces (
    id         uuid PRIMARY KEY,
    slug       text        NOT NULL UNIQUE,
    name       text        NOT NULL,
    kind       text        NOT NULL,
    owner_id   uuid        NOT NULL REFERENCES identity.users(id),
    created_at timestamptz NOT NULL
);

CREATE TABLE identity.memberships (
    workspace_id uuid        NOT NULL REFERENCES identity.workspaces(id),
    user_id      uuid        NOT NULL REFERENCES identity.users(id),
    role         text        NOT NULL,
    joined_at    timestamptz NOT NULL,
    PRIMARY KEY (workspace_id, user_id)
);

CREATE TABLE identity.instance_settings (
    id                   int PRIMARY KEY DEFAULT 1 CHECK (id = 1),
    name                 text        NOT NULL DEFAULT 'MLPlatform',
    allow_signup         boolean     NOT NULL DEFAULT false,
    ray_client_enabled   boolean     NOT NULL DEFAULT true,
    bootstrap_token_hash text,
    bootstrap_expires_at timestamptz,
    bootstrapped_at      timestamptz
);
INSERT INTO identity.instance_settings (id) VALUES (1) ON CONFLICT DO NOTHING;
