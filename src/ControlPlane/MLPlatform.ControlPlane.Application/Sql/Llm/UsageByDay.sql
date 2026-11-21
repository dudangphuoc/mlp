-- mlp:query UsageByDay :many
SELECT date_trunc('day', u.at) AS day, u.deployment_id, d.slug, count(*) AS calls, sum(u.prompt_tokens)::bigint AS prompt_tokens,
  sum(u.completion_tokens)::bigint AS completion_tokens, sum(u.cost)::double precision AS cost, avg(u.latency_ms)::double precision AS avg_latency_ms,
  count(*) FILTER (WHERE u.blocked) AS blocked
FROM llmops.usage u JOIN serving.deployments d ON d.id = u.deployment_id
WHERE u.workspace_id = @workspace_id AND u.at > @since
GROUP BY 1, 2, 3 ORDER BY 1, 3;
