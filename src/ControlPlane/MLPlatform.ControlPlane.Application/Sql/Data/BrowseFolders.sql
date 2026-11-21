-- mlp:query BrowseFolders :many
-- Direct subfolders of a prefix with the number of files below them.
WITH f AS (
  SELECT path, size_bytes, url FROM data.version_files WHERE version_id = @version_id
  UNION ALL
  SELECT path, size_bytes, NULL::text AS url FROM data.files WHERE @version_id::uuid IS NULL AND dataset_id = @dataset_id)
SELECT split_part(substr(path, @prefix_len + 1), '/', 1) AS name, count(*) AS files, coalesce(sum(size_bytes), 0)::bigint AS size_bytes
FROM f WHERE path LIKE @prefix_like AND position('/' in substr(path, @prefix_len + 1)) > 0
GROUP BY 1 ORDER BY 1 LIMIT 1000;
