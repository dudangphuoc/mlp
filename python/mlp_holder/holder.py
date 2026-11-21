"""Ray job that holds resources for an attempt (cookbook 20/08 §9, M03 R1, D-21).

Runs via the Ray Jobs API on the platform cluster (the driver runs on the head without using the head's CPU, since
the head declares --num-cpus=0; the driver requests no resources). Steps:
  1. Create a placement group: one bundle per machine {CPU, GPU, memory, host:<id>: 0.001} (STRICT_PACK / STRICT_SPREAD).
     A bundle with "gpu_fraction" (0 < x < 1) reserves that share of ONE GPU (D-50: several jobs share a GPU by memory budget).
  2. One Holder actor per bundle requests exactly the bundle's resources → reads the GPUs Ray assigned (UUIDs when the node starts with
     CUDA_VISIBLE_DEVICES=<UUID…>) → calls RuntimeService.ReportAllocation on the api.
  3. Holds until the api stops the Ray job (release = stop the job → actors + placement group are deleted).
Re-hold (`--rehold`): the head restarted while a workload kept running (D-40), so its reservation is gone. Take the same CPU/memory and the
same GPUs (by UUID) again without launching anything, so the scheduler cannot hand them to another job.
No dependency on generated Python code: the small proto messages are hand-encoded (wire format) and sent via the grpcio already in the Ray image.
"""
from __future__ import annotations

import argparse
import json
import signal
import sys
import time

import grpc
import ray
from ray.util.placement_group import placement_group
from ray.util.scheduling_strategies import PlacementGroupSchedulingStrategy


def _varint(n: int) -> bytes:
    out = bytearray()
    while True:
        b = n & 0x7F
        n >>= 7
        out.append(b | (0x80 if n else 0))
        if not n:
            return bytes(out)


def _str_field(num: int, s: str) -> bytes:
    data = s.encode()
    return _varint((num << 3) | 2) + _varint(len(data)) + data


def encode_allocation(attempt_id: str, host_id: str, gpu_uuids: list[str], node_ip: str, ok: bool, message: str) -> bytes:
    # ReportAllocationRequest{attempt_id=1, host_id=2, repeated gpu_uuids=3, node_ip=4, ok=5, message=6}
    msg = _str_field(1, attempt_id) + _str_field(2, host_id)
    for g in gpu_uuids:
        msg += _str_field(3, g)
    msg += _str_field(4, node_ip)
    msg += _varint((5 << 3) | 0) + _varint(1 if ok else 0)
    if message:
        msg += _str_field(6, message)
    return msg


def report(api: str, token: str, payload: bytes) -> None:
    with grpc.insecure_channel(api) as ch:
        call = ch.unary_unary("/mlplatform.v1.RuntimeService/ReportAllocation",
                              request_serializer=None, response_deserializer=None)
        call(payload, metadata=[("authorization", f"Bearer {token}")], timeout=30)


@ray.remote(num_cpus=0)
class Holder:
    def info(self) -> dict:
        ctx = ray.get_runtime_context()
        gpus = ray.get_runtime_context().get_accelerator_ids().get("GPU", [])
        return {"node_ip": ray.util.get_node_ip_address(), "gpus": [str(g) for g in gpus], "node_id": ctx.get_node_id()}


def _take(opts: dict, timeout: float):
    """Start one Holder actor; None when the resources are not free within the timeout."""
    h = Holder.options(**opts).remote()
    try:
        return h, ray.get(h.info.remote(), timeout=timeout)
    except ray.exceptions.GetTimeoutError:
        ray.kill(h)
        return None


def rehold(todo: list, kept: list, timeout: float) -> list:
    """Reserve what running workloads already use; returns what is still missing. Ray picks GPUs itself, so take them one at a time and
    keep only the wanted UUIDs (the others are released at the end of the pass)."""
    left = []
    for b in todo:
        host = {f"host:{b['host_id']}": 0.001}
        if not b.get("cpu_held"):
            opts = {"num_cpus": float(b["cpus"]), "resources": host}
            if b.get("memory_bytes"):
                opts["memory"] = float(b["memory_bytes"])
            got = _take(opts, timeout)
            if got is None:
                print(f"rehold {b['host_id']}: CPU/memory not free yet", flush=True)
            else:
                kept.append(got[0])
                b["cpu_held"] = True
        wanted, extra = set(b.get("gpu_uuids") or []), []
        while wanted:
            got = _take({"num_cpus": 0, "num_gpus": float(b.get("gpu_fraction") or 1), "resources": host}, timeout)
            if got is None:
                print(f"rehold {b['host_id']}: GPU not free yet: {sorted(wanted)}", flush=True)
                break
            mine = [g for g in got[1]["gpus"] if g in wanted]
            if mine:
                wanted.difference_update(mine)
                kept.append(got[0])
                print(f"rehold {b['host_id']}: gpu {mine}", flush=True)
            else:
                extra.append(got[0])
        for h in extra:
            ray.kill(h)
        b["gpu_uuids"] = sorted(wanted)
        if wanted or not b.get("cpu_held"):
            left.append(b)
    return left


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--attempt", required=True)
    p.add_argument("--api", default="")         # api gRPC host:port (over the overlay)
    p.add_argument("--token", default="")      # mlpj_…
    p.add_argument("--bundles", required=True)      # JSON [{"host_id","cpus","gpus","gpu_fraction","memory_bytes"}]
    p.add_argument("--strategy", default="STRICT_PACK")
    p.add_argument("--ready-timeout", type=float, default=120)
    p.add_argument("--rehold", action="store_true")  # bundles carry "gpu_uuids"; nothing is reported or launched
    a = p.parse_args()
    bundles = json.loads(a.bundles)

    ray.init(address="auto", log_to_driver=False)
    stop = {"v": False}
    signal.signal(signal.SIGTERM, lambda *_: stop.__setitem__("v", True))
    if a.rehold:
        kept, todo, passes = [], bundles, 0  # the actors in `kept` live as long as this job
        while not stop["v"]:
            if todo and passes < 40:  # the machine may still be rejoining the head: keep trying for about ten minutes
                todo, passes = rehold(todo, kept, 30 if passes == 0 else 10), passes + 1
            time.sleep(5)
        return 0
    specs = []
    for b in bundles:
        spec = {"CPU": float(b["cpus"]), f"host:{b['host_id']}": 0.001}
        if b.get("gpus"):
            spec["GPU"] = float(b.get("gpu_fraction") or b["gpus"])
        if b.get("memory_bytes"):
            spec["memory"] = float(b["memory_bytes"])
        specs.append(spec)
    pg = placement_group(specs, strategy=a.strategy, name=f"mlp-{a.attempt}")
    try:
        ray.get(pg.ready(), timeout=a.ready_timeout)
    except ray.exceptions.GetTimeoutError:
        for b in bundles:
            report(a.api, a.token, encode_allocation(a.attempt, b["host_id"], [], "", False, "placement_timeout"))
        return 1

    holders = []
    for i, (b, spec) in enumerate(zip(bundles, specs)):
        opts = {
            "num_cpus": spec["CPU"],
            "num_gpus": spec.get("GPU", 0),
            "resources": {f"host:{b['host_id']}": 0.001},
            "scheduling_strategy": PlacementGroupSchedulingStrategy(placement_group=pg, placement_group_bundle_index=i),
        }
        if "memory" in spec:
            opts["memory"] = spec["memory"]
        holders.append((b, Holder.options(**opts).remote()))
    for b, h in holders:
        info = ray.get(h.info.remote())
        report(a.api, a.token, encode_allocation(a.attempt, b["host_id"], info["gpus"], info["node_ip"], True, ""))
        print(f"holder {b['host_id']}: node {info['node_ip']} gpus={info['gpus']}", flush=True)

    while not stop["v"]:
        time.sleep(5)
    return 0


if __name__ == "__main__":
    sys.exit(main())
