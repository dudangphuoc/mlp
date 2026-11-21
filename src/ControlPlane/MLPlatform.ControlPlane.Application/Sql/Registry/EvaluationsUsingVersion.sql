-- mlp:query EvaluationsUsingVersion :scalar
SELECT count(*) FROM registry.evaluations WHERE dataset_version_id = @version_id AND workspace_id = @workspace_id;
