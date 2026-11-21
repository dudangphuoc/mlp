-- The working copy becomes the pending change of the next version: files added since the latest version, and paths to drop from it.
-- A new version = latest version − removals + working copy; the working copy is then empty again.
CREATE TABLE data.pending_removals (
    dataset_id uuid        NOT NULL REFERENCES data.datasets(id),
    path       text        NOT NULL,
    at         timestamptz NOT NULL,
    PRIMARY KEY (dataset_id, path)
);

-- Until now the working copy kept every file ever uploaded, so each version was a full snapshot of it. Files already frozen unchanged
-- into the latest version are not pending any more.
DELETE FROM data.files f
USING data.dataset_versions v, data.version_files vf
WHERE v.dataset_id = f.dataset_id
  AND v.number = (SELECT max(v2.number) FROM data.dataset_versions v2 WHERE v2.dataset_id = f.dataset_id)
  AND vf.version_id = v.id AND vf.path = f.path AND vf.size_bytes = f.size_bytes;
