-- Scripts of a git repository that can be started from the web: which file, from which branch by default, on which machine.
CREATE TABLE track.code_entries (
    id           uuid PRIMARY KEY,
    workspace_id uuid        NOT NULL,
    project_id   uuid        NOT NULL REFERENCES track.projects(id),
    name         text        NOT NULL,
    git_remote   text        NOT NULL,
    ref          text        NOT NULL,
    script       text        NOT NULL,
    machine      text        NOT NULL,
    gpus         int         NOT NULL,
    image        text        NOT NULL,
    created_by   uuid        NOT NULL,
    created_at   timestamptz NOT NULL,
    UNIQUE (project_id, name)
);
