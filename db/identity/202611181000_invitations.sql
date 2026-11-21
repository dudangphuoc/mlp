-- M01 §5.3: workspace invitations (single-use token, 7 days). Sign-up via invitation is allowed even when self sign-up is disabled.
CREATE TABLE identity.invitations (
    id           uuid PRIMARY KEY,
    workspace_id uuid        NOT NULL REFERENCES identity.workspaces(id),
    email        text        NOT NULL,
    role         text        NOT NULL,
    token_hash   text        NOT NULL UNIQUE,
    created_by   uuid        NOT NULL,
    created_at   timestamptz NOT NULL,
    expires_at   timestamptz NOT NULL,
    accepted_at  timestamptz
);
