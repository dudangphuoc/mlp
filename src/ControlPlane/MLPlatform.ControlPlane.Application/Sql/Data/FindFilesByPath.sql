-- mlp:query FindFilesByPath :many
WITH f AS (
  SELECT path, size_bytes, url FROM data.version_files WHERE version_id = @version_id
  UNION ALL
  SELECT path, size_bytes, NULL::text AS url FROM data.files WHERE @version_id::uuid IS NULL AND dataset_id = @dataset_id)
SELECT path, size_bytes, url FROM f WHERE path = ANY(@paths);
