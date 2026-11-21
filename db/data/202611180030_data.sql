-- M04 §3 (first slice, file-based datasets; detailed items/labels added with M05)
CREATE SCHEMA IF NOT EXISTS data;

CREATE TABLE data.datasets (
    id           uuid PRIMARY KEY,
    workspace_id uuid        NOT NULL,
    slug         text        NOT NULL,
    name         text        NOT NULL,
    task         text        NOT NULL,
    owner_id     uuid        NOT NULL,
    status       text        NOT NULL,
    created_at   timestamptz NOT NULL,
    UNIQUE (workspace_id, slug)
);

CREATE TABLE data.files (
    dataset_id  uuid        NOT NULL REFERENCES data.datasets(id),
    path        text        NOT NULL,
    size_bytes  bigint      NOT NULL,
    uploaded_at timestamptz NOT NULL,
    PRIMARY KEY (dataset_id, path)
);

CREATE TABLE data.dataset_versions (
    id           uuid PRIMARY KEY,
    dataset_id   uuid        NOT NULL REFERENCES data.datasets(id),
    number       int         NOT NULL,
    prefix       text        NOT NULL,     -- prefix in the mlp-datasets bucket (immutable copy)
    file_count   bigint      NOT NULL,
    size_bytes   bigint      NOT NULL,
    note         text        NOT NULL,
    created_by   uuid        NOT NULL,
    created_at   timestamptz NOT NULL,
    UNIQUE (dataset_id, number)
);

CREATE TABLE data.version_files (
    version_id uuid   NOT NULL REFERENCES data.dataset_versions(id),
    path       text   NOT NULL,
    size_bytes bigint NOT NULL,
    PRIMARY KEY (version_id, path)
);
