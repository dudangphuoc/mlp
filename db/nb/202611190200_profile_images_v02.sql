-- Notebook image 0.2: runs as user mlp (Jupyter refuses root) — new tag so machines that built 0.1 rebuild.
UPDATE nb.profiles SET image_cpu = replace(image_cpu, ':0.1-', ':0.2-'), image_gpu = replace(image_gpu, ':0.1-', ':0.2-');
