-- mlp:query FileExtensionStats :many
WITH f AS (
  SELECT path, size_bytes, url FROM data.version_files WHERE version_id = @version_id
  UNION ALL
  SELECT path, size_bytes, NULL::text AS url FROM data.files WHERE @version_id::uuid IS NULL AND dataset_id = @dataset_id)
SELECT coalesce(lower(substring(path from '\.([^./]+)$')), '') AS ext, count(*) AS files, coalesce(sum(size_bytes), 0)::bigint AS size_bytes
FROM f GROUP BY 1 ORDER BY 2 DESC LIMIT 100;
