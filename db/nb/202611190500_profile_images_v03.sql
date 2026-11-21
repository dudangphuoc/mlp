-- Notebook image 0.3: dark JupyterLab theme + Jupyter news prompt disabled — new tag so machines that built 0.2 rebuild.
UPDATE nb.profiles SET image_cpu = replace(image_cpu, ':0.2-', ':0.3-'), image_gpu = replace(image_gpu, ':0.2-', ':0.3-');
