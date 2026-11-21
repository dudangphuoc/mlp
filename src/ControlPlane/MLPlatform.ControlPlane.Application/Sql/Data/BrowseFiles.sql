-- mlp:query BrowseFiles :many
-- Content browser: files of one version (or the working copy when version_id is null) under a folder prefix,
-- optionally recursive, filtered by name and by extension; paged.
WITH f AS (
  SELECT path, size_bytes, url FROM data.version_files WHERE version_id = @version_id
  UNION ALL
  SELECT path, size_bytes, NULL::text AS url FROM data.files WHERE @version_id::uuid IS NULL AND dataset_id = @dataset_id)
SELECT path, size_bytes, url FROM f
WHERE path LIKE @prefix_like
  AND (@recursive OR position('/' in substr(path, @prefix_len + 1)) = 0)
  AND (@q_like = '' OR path ILIKE @q_like)
  AND (NOT @use_exts OR lower(substring(path from '\.([^./]+)$')) = ANY(@exts))
ORDER BY path OFFSET @offset LIMIT @limit;
