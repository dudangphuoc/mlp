/** Ready-made pipelines: one click creates a complete, reproducible workflow (data → train → check → register). */
export type PipelineTemplate = {
  key: string;
  name: string;            // default pipeline name
  title: string;           // English UI text (translated with t())
  description: string;
  definition: object;
};

export const PIPELINE_TEMPLATES: PipelineTemplate[] = [
  {
    key: "fire-dfine",
    name: "fire-detection-dfine",
    title: "Fire detection · D-FINE",
    description: "Imports the Wild-Fire dataset (Hugging Face, Apache-2.0, ~1,360 photos with fire/smoke boxes), fine-tunes D-FINE-S from COCO weights on one GPU, checks mAP and registers the model.",
    definition: {
      params: {
        hf_repo: "baizhanquan/firesafety-wildfire",
        revision: "main",
        classes: ["smoke", "fire"],
        variant: "dfine-s",
        epochs: 24,
        batch: 8,
        gpus: 1,
        min_map: 0.2,
        model_name: "fire-detector",
      },
      steps: [
        {
          id: "data", kind: "dataset",
          config: {
            source: "hf", repo: "${params.hf_repo}", revision: "${params.revision}", mode: "reference",
            include: ["data/train/images/*", "data/train/yololabels/*"],
            name: "fire-wildfire", task: "detect", classes: "${params.classes}",
          },
        },
        {
          id: "train", kind: "train", needs: ["data"], retries: 1,
          config: {
            project_name: "fire-detection", task: "detect", recipe: "d-fine", variant: "${params.variant}",
            dataset_version_id: "${steps.data.dataset_version_id}",
            params: { epochs: "${params.epochs}", batch: "${params.batch}" },
            gpus: "${params.gpus}", cpus: 1,
          },
        },
        { id: "check", kind: "gate", needs: ["train"], config: { left: "${steps.train.metric}", op: ">=", right: "${params.min_map}" } },
        { id: "register", kind: "register", needs: ["check"], config: { run_id: "${steps.train.run_id}", model_name: "${params.model_name}" } },
      ],
    },
  },
];
