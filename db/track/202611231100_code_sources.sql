-- Git repositories (by URL prefix) the server may fetch code from, with the credential to use. Longest matching prefix wins.
CREATE TABLE track.code_sources (
    id           uuid PRIMARY KEY,
    workspace_id uuid        NOT NULL,
    url_prefix   text        NOT NULL,
    secret_enc   text,
    created_by   uuid        NOT NULL,
    created_at   timestamptz NOT NULL,
    UNIQUE (workspace_id, url_prefix)
);
