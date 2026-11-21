-- Import modes (M04/M11): "copied" = files live in our S3 storage; "reference" = only the source address is stored and the
-- machine downloads the files from the source when a job uses the version.
ALTER TABLE data.dataset_versions ADD COLUMN IF NOT EXISTS storage text NOT NULL DEFAULT 'copied';
ALTER TABLE data.dataset_versions ADD COLUMN IF NOT EXISTS source_ref text;
ALTER TABLE data.version_files ADD COLUMN IF NOT EXISTS url text;            -- reference: https://… or s3://bucket/key

ALTER TABLE registry.versions ADD COLUMN IF NOT EXISTS storage text NOT NULL DEFAULT 'copied';
CREATE TABLE IF NOT EXISTS registry.version_files (
    version_id uuid   NOT NULL REFERENCES registry.versions(id),
    path       text   NOT NULL,
    size_bytes bigint NOT NULL,
    url        text   NOT NULL,
    PRIMARY KEY (version_id, path)
);

ALTER TABLE hub.imports ADD COLUMN IF NOT EXISTS source text NOT NULL DEFAULT 'hf';    -- hf | url | s3
ALTER TABLE hub.imports ADD COLUMN IF NOT EXISTS mode   text NOT NULL DEFAULT 'copy';  -- copy | reference
