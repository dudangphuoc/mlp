-- Start again / change version keeps the machine the deployment was created with.
ALTER TABLE serving.deployments ADD COLUMN cpus double precision;
ALTER TABLE serving.deployments ADD COLUMN gpus int;            -- null = follow the recipe
ALTER TABLE serving.deployments ADD COLUMN image_override text;
