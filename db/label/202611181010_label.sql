-- M05 §3 (first slice). Item = a dataset file (key: dataset_id + path).
CREATE SCHEMA IF NOT EXISTS label;

CREATE TABLE label.schemas (
    id          uuid PRIMARY KEY,
    dataset_id  uuid        NOT NULL REFERENCES data.datasets(id),
    version     int         NOT NULL,
    task        text        NOT NULL,
    classes     jsonb       NOT NULL,
    rules       jsonb       NOT NULL DEFAULT '{}',
    is_current  boolean     NOT NULL,
    created_by  uuid        NOT NULL,
    created_at  timestamptz NOT NULL,
    UNIQUE (dataset_id, version)
);

CREATE TABLE label.projects (
    id             uuid PRIMARY KEY,
    workspace_id   uuid        NOT NULL,
    dataset_id     uuid        NOT NULL REFERENCES data.datasets(id),
    name           text        NOT NULL,
    guideline_md   text        NOT NULL,
    schema_version int         NOT NULL,
    review_mode    text        NOT NULL,          -- none | all
    lock_ttl_min   int         NOT NULL,
    state          text        NOT NULL,          -- draft | active | paused | completed
    created_by     uuid        NOT NULL,
    created_at     timestamptz NOT NULL
);

CREATE TABLE label.project_members (
    project_id     uuid NOT NULL REFERENCES label.projects(id),
    user_id        uuid NOT NULL,
    role           text NOT NULL,                 -- lead | annotator | reviewer
    max_open_items int  NOT NULL DEFAULT 50,
    PRIMARY KEY (project_id, user_id, role)
);

CREATE TABLE label.item_states (
    project_id        uuid        NOT NULL REFERENCES label.projects(id),
    dataset_id        uuid        NOT NULL,
    path              text        NOT NULL,
    status            text        NOT NULL,
    annotator_id      uuid,
    reviewer_id       uuid,
    priority_score    double precision NOT NULL DEFAULT 0,
    lock_owner        uuid,
    lock_expires_at   timestamptz,
    draft_by          uuid,
    draft_annotations jsonb,
    draft_saved_at    timestamptz,
    skip_reason       text,
    submitted_at      timestamptz,
    reviewed_at       timestamptz,
    revision          int         NOT NULL DEFAULT 0,
    PRIMARY KEY (project_id, path)
);
CREATE INDEX ix_item_states_queue ON label.item_states (project_id, status, priority_score DESC);

CREATE TABLE label.annotations (
    id          uuid PRIMARY KEY,
    dataset_id  uuid        NOT NULL,
    path        text        NOT NULL,
    schema_version int      NOT NULL,
    kind        text        NOT NULL,
    class_id    int         NOT NULL,
    geometry    jsonb       NOT NULL,
    attributes  jsonb       NOT NULL,
    source      text        NOT NULL,             -- human | model | import
    confidence  double precision,
    created_by  uuid        NOT NULL,
    created_at  timestamptz NOT NULL,
    deleted_at  timestamptz
);
CREATE INDEX ix_annotations_item ON label.annotations (dataset_id, path) WHERE deleted_at IS NULL;

CREATE TABLE label.annotation_history (
    id         uuid PRIMARY KEY,
    dataset_id uuid        NOT NULL,
    path       text        NOT NULL,
    action     text        NOT NULL,
    before     jsonb,
    after      jsonb,
    actor_id   uuid        NOT NULL,
    at         timestamptz NOT NULL
);

CREATE TABLE label.comments (
    id         uuid PRIMARY KEY,
    project_id uuid        NOT NULL,
    path       text        NOT NULL,
    author_id  uuid        NOT NULL,
    body       text        NOT NULL,
    resolved   boolean     NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL
);

CREATE TABLE label.label_sets (
    id             uuid PRIMARY KEY,
    dataset_id     uuid        NOT NULL REFERENCES data.datasets(id),
    number         int         NOT NULL,
    schema_version int         NOT NULL,
    snapshot_key   text        NOT NULL,          -- JSONL in S3 storage (Parquet later)
    item_count     bigint      NOT NULL,
    annotation_count bigint    NOT NULL,
    include_status text        NOT NULL,          -- accepted_only | all_labeled
    created_by     uuid        NOT NULL,
    created_at     timestamptz NOT NULL,
    UNIQUE (dataset_id, number)
);
