-- Version numbers are never reused: a deleted latest version's number (and its v{n}/ storage keys) is not given to the next version,
-- so every dataset object key is written once. Hosts cache dataset files by key (see DataCacheRules), which needs that guarantee.
ALTER TABLE data.datasets ADD COLUMN version_seq int NOT NULL DEFAULT 0;
UPDATE data.datasets d SET version_seq = coalesce((SELECT max(number) FROM data.dataset_versions v WHERE v.dataset_id = d.id), 0);
