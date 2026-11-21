import type { JsonValue } from "@bufbuild/protobuf";

const slugs = new Map<string, string>();
const tasks = new Map<string, string>();

export const DeploymentSlugs = {
  remember(rows: Record<string, JsonValue>[]) {
    for (const r of rows) {
      if (typeof r.slug === "string") slugs.set(String(r.id), r.slug);
      if (typeof r.task === "string") tasks.set(String(r.id), r.task);
    }
  },
  get: (id: string) => slugs.get(id),
  task: (id: string) => tasks.get(id) ?? "",
};
