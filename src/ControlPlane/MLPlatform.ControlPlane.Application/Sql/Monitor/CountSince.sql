-- mlp:query CountSince :scalar
SELECT count(*) FROM monitor.predictions WHERE deployment_id = @deployment_id AND at > @since;
