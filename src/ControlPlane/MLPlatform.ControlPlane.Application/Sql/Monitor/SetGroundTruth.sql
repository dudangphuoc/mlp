-- mlp:query SetGroundTruth :scalar
WITH u AS (UPDATE monitor.predictions SET ground_truth = @ground_truth::jsonb, gt_at = @now
           WHERE id = @id AND deployment_id = @deployment_id RETURNING 1)
SELECT count(*) FROM u;
