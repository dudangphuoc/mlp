-- mlp:query GetProfile :one
SELECT name, display_name, description, image_cpu, image_gpu, version FROM nb.profiles WHERE name = @name AND enabled;
