-- mlp:query CountBrowseFiles :scalar
WITH f AS (
  SELECT path, size_bytes, url FROM data.version_files WHERE version_id = @version_id
  UNION ALL
  SELECT path, size_bytes, NULL::text AS url FROM data.files WHERE @version_id::uuid IS NULL AND dataset_id = @dataset_id)
SELECT count(*) FROM f
WHERE path LIKE @prefix_like
  AND (@recursive OR position('/' in substr(path, @prefix_len + 1)) = 0)
  AND (@q_like = '' OR path ILIKE @q_like)
  AND (NOT @use_exts OR lower(substring(path from '\.([^./]+)$')) = ANY(@exts));
