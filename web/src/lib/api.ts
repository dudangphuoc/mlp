// gRPC-Web (Connect-ES) client for every service (D-10, D-13). The session cookie is sent automatically.
import { createClient, ConnectError, Code } from "@connectrpc/connect";
import { translate } from "../i18n";
import { createGrpcWebTransport } from "@connectrpc/connect-web";
import { IamService, WorkspaceService, InstanceService } from "../gen/mlplatform/v1/iam_pb";
import { FleetService } from "../gen/mlplatform/v1/fleet_pb";
import { SchedulerService } from "../gen/mlplatform/v1/scheduler_pb";
import { CatalogService } from "../gen/mlplatform/v1/catalog_pb";
import { TrackingService } from "../gen/mlplatform/v1/tracking_pb";
import { DataService } from "../gen/mlplatform/v1/data_pb";
import { RegistryService } from "../gen/mlplatform/v1/registry_pb";
import { ServingService } from "../gen/mlplatform/v1/serving_pb";
import { LabelingService } from "../gen/mlplatform/v1/labeling_pb";
import { NotebookService } from "../gen/mlplatform/v1/notebook_pb";
import { NotificationService } from "../gen/mlplatform/v1/notify_pb";
import { TuningService } from "../gen/mlplatform/v1/tuning_pb";
import { MonitoringService } from "../gen/mlplatform/v1/monitoring_pb";
import { PipelineService } from "../gen/mlplatform/v1/pipeline_pb";
import { LlmOpsService } from "../gen/mlplatform/v1/llmops_pb";
import { FeatureService } from "../gen/mlplatform/v1/feature_pb";
import { OpsService } from "../gen/mlplatform/v1/ops_pb";
import { AuditService } from "../gen/mlplatform/v1/audit_pb";
import { HubService } from "../gen/mlplatform/v1/hub_pb";
import { SettingsService } from "../gen/mlplatform/v1/settings_pb";
import { AiService } from "../gen/mlplatform/v1/ai_pb";

const transport = createGrpcWebTransport({
  baseUrl: window.location.origin,
  fetch: (input, init) => fetch(input, { ...init, credentials: "include" }),
});

export const api = {
  iam: createClient(IamService, transport),
  workspace: createClient(WorkspaceService, transport),
  instance: createClient(InstanceService, transport),
  fleet: createClient(FleetService, transport),
  scheduler: createClient(SchedulerService, transport),
  catalog: createClient(CatalogService, transport),
  tracking: createClient(TrackingService, transport),
  data: createClient(DataService, transport),
  registry: createClient(RegistryService, transport),
  serving: createClient(ServingService, transport),
  labeling: createClient(LabelingService, transport),
  notebook: createClient(NotebookService, transport),
  notify: createClient(NotificationService, transport),
  tuning: createClient(TuningService, transport),
  monitoring: createClient(MonitoringService, transport),
  pipeline: createClient(PipelineService, transport),
  llm: createClient(LlmOpsService, transport),
  feature: createClient(FeatureService, transport),
  ops: createClient(OpsService, transport),
  audit: createClient(AuditService, transport),
  hub: createClient(HubService, transport),
  settings: createClient(SettingsService, transport),
  ai: createClient(AiService, transport),
};

/** User-facing error text: the server message (English) translated by the UI table, plus the reason code for lookup. */
export function errorText(e: unknown): string {
  if (e instanceof ConnectError) {
    const reason = e.metadata.get("mlp-error-reason");
    const msg = translate(e.rawMessage);
    // Validation errors carry one trailer per field (mlp-field-{name}: url-encoded message).
    const fields: string[] = [];
    e.metadata.forEach((v, k) => {
      if (!k.startsWith("mlp-field-")) return;
      let text = v; try { text = decodeURIComponent(v); } catch { /* keep raw */ }
      fields.push(translate(text));
    });
    const head = reason ? `${msg} (${reason})` : msg;
    return fields.length ? `${head}: ${fields.join("; ")}` : head;
  }
  return translate(e instanceof Error ? e.message : String(e));
}

export const isUnauthenticated = (e: unknown) => e instanceof ConnectError && e.code === Code.Unauthenticated;
/** Optimistic-lock conflict: the thing was saved somewhere else since it was read. */
export const isAborted = (e: unknown) => e instanceof ConnectError && e.code === Code.Aborted;

/** Server reason code of a failed call (e.g. LABEL_EXTERNAL_CONFIRM), or "". */
export const errorReason = (e: unknown) => (e instanceof ConnectError ? e.metadata.get("mlp-error-reason") ?? "" : "");
