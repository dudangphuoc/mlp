-- mlp:query ListProfiles :many
SELECT name, display_name, description, image_cpu, image_gpu, version FROM nb.profiles WHERE enabled ORDER BY sort;
