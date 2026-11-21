-- mlp:query SeedItemsFromVersion :scalar
-- Items are the files a person labels: images for image tasks (not the label txt, data.yaml or split file next to them).
WITH ins AS (
  INSERT INTO label.item_states (project_id, dataset_id, path, status)
  SELECT @project_id, @dataset_id, f.path, 'unlabeled' FROM data.version_files f WHERE f.version_id = @version_id
  AND (CASE WHEN @images_only THEN lower(substring(f.path from '\.([^./]+)$')) = ANY(@exts)
            ELSE coalesce(lower(substring(f.path from '\.([^./]+)$')), '') NOT IN ('yaml', 'yml', 'json') END)
  ON CONFLICT DO NOTHING RETURNING 1)
SELECT count(*) FROM ins;
