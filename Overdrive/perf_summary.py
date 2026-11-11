#!/usr/bin/env python3
"""Summarise a BDArmory Overdrive perf probe CSV (GameData/BDArmory/PluginData/PerfProbe/perf_*.csv).

Usage:
    python perf_summary.py perf_20261112_201500.csv [--min-missiles N] [--competition-only] [--skip-seconds S]

Prints a Markdown summary: frame time, how the frame splits into phases (FixedUpdate scripts, physics,
Update/LateUpdate scripts, everything else), BDArmory vs non-BDArmory time, the top BDArmory sections,
frame time by missiles in flight and by part count, physics steps per frame, and GC pauses.
Standard library only. See Overdrive/PERF.md for the column meanings.
"""
import argparse
import csv
import math
import statistics
import sys

# Which loop each BDArmory section runs in (see PerfSection in BDArmory/Utils/PerfProbe.cs).
FIXED = ["wm_fixed", "wm_pd", "missile_fixed", "mmg_fixed", "ai_fbw", "ai_fixed", "radar_fixed", "rwr_fixed",
         "irst_fixed", "weapon_fixed", "bullets", "rockets", "hp_fixed", "competition"]
UPDATE = ["wm_update", "radar_update", "weapon_update", "setup_update", "target_manager"]
GUI = ["wm_gui", "missile_gui", "setup_gui"]
MIXED = ["vrd", "explosions"]  # Update + FixedUpdate.
NESTED = ["guard_scan", "target_selection", "heat_target", "radar_missile_lock", "radar_scan_lock", "can_see_target"]


def pct(values, p):
    if not values:
        return float("nan")
    s = sorted(values)
    k = (len(s) - 1) * p / 100.0
    lo, hi = math.floor(k), math.ceil(k)
    return s[lo] if lo == hi else s[lo] + (s[hi] - s[lo]) * (k - lo)


def mean(values):
    return statistics.fmean(values) if values else float("nan")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("csv")
    ap.add_argument("--min-missiles", type=int, default=0, help="only frames with at least N missiles in flight")
    ap.add_argument("--competition-only", action="store_true", help="only frames while a competition is running")
    ap.add_argument("--skip-seconds", type=float, default=0.0, help="ignore the first S seconds (real time) of the recording")
    args = ap.parse_args()

    with open(args.csv, newline="", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        rows = []
        for r in reader:
            try:
                row = {k: float(v) for k, v in r.items() if k is not None and v not in (None, "")}
            except ValueError:
                continue  # A partial last line (KSP closed without flushing).
            if "frame_ms" not in row or "probe_ms" not in row:
                continue
            rows.append(row)
    total_rows = len(rows)
    rows = [r for r in rows
            if r["real_time"] >= args.skip_seconds
            and r["missiles_in_flight"] >= args.min_missiles
            and (not args.competition_only or r["competition_active"] > 0)]
    if not rows:
        sys.exit(f"No rows match the filters ({total_rows} rows in the file).")

    def col(name):
        return [r.get(name, 0.0) for r in rows]

    frame = col("frame_ms")
    duration_s = sum(frame) / 1000.0
    print(f"# Perf probe summary: {args.csv}")
    print()
    print(f"Frames: {len(rows)} of {total_rows} (filters: min missiles {args.min_missiles}, competition only {args.competition_only}, skip {args.skip_seconds:g} s); "
          f"{duration_s:.1f} s of frames; game time advanced {max(col('game_time')) - min(col('game_time')):.1f} s.")
    print(f"Average fps: {len(rows) / duration_s:.1f} (median frame {pct(frame, 50):.1f} ms -> {1000.0 / max(pct(frame, 50), 1e-6):.1f} fps).")
    print()
    print("| metric | mean | p50 | p95 | p99 | max |")
    print("|---|---|---|---|---|---|")
    for name in ["frame_ms", "bda_ms", "non_bda_ms", "fixed_scripts_ms", "physics_ms", "update_ms", "other_ms", "probe_ms"]:
        v = col(name)
        print(f"| {name} | {mean(v):.2f} | {pct(v, 50):.2f} | {pct(v, 95):.2f} | {pct(v, 99):.2f} | {max(v):.2f} |")
    print()

    f_mean = mean(frame)
    bda = mean(col("bda_ms"))
    bda_fixed = sum(mean(col(n + "_ms")) for n in FIXED)
    bda_update = sum(mean(col(n + "_ms")) for n in UPDATE)
    bda_gui = sum(mean(col(n + "_ms")) for n in GUI)
    bda_mixed = sum(mean(col(n + "_ms")) for n in MIXED)
    fixed_scripts = mean(col("fixed_scripts_ms"))
    physics = mean(col("physics_ms"))
    update = mean(col("update_ms"))
    other = mean(col("other_ms"))
    print("## Where the frame goes (means per frame)")
    print()
    print(f"- BDArmory (instrumented entry points): {bda:.2f} ms = {100 * bda / f_mean:.0f}% of the frame.")
    print(f"- Everything else (KSP, PhysX, rendering, other mods, BDA coroutines): {f_mean - bda:.2f} ms = {100 * (f_mean - bda) / f_mean:.0f}%.")
    print(f"- FixedUpdate scripts: {fixed_scripts:.2f} ms, of which BDArmory about {bda_fixed:.2f} ms "
          f"-> KSP + other mods' FixedUpdate about {max(0.0, fixed_scripts - bda_fixed):.2f} ms (FlightIntegrator, part modules, vessel updates).")
    print(f"- Physics step (PhysX + collision callbacks + early WaitForFixedUpdate coroutines): {physics:.2f} ms.")
    print(f"- Update/LateUpdate scripts: {update:.2f} ms, of which BDArmory about {bda_update:.2f} ms.")
    print(f"- Everything else (rendering, OnGUI, GC, vsync wait): {other:.2f} ms, of which BDArmory OnGUI bodies {bda_gui:.2f} ms.")
    print(f"- BDArmory sections in both loops (vrd, explosions): {bda_mixed:.2f} ms.")
    steps = col("fixed_steps")
    print(f"- Physics steps per frame: mean {mean(steps):.2f}, max {max(steps):.0f}; per step: FixedUpdate scripts {fixed_scripts / max(mean(steps), 1e-6):.2f} ms, physics {physics / max(mean(steps), 1e-6):.2f} ms.")
    print()

    print("## BDArmory sections (mean ms per frame, calls per frame)")
    print()
    print("| section | ms | % of frame | calls | us per call |")
    print("|---|---|---|---|---|")
    sections = [n[:-3] for n in rows[0].keys() if n.endswith("_ms") and (n[:-3] + "_n") in rows[0]]
    stats = []
    for n in sections:
        ms, calls = mean(col(n + "_ms")), mean(col(n + "_n"))
        stats.append((ms, calls, n))
    for ms, calls, n in sorted(stats, reverse=True):
        if ms <= 0 and calls <= 0:
            continue
        nested = " (nested)" if n in NESTED else ""
        per_call = 1000.0 * ms / calls if calls > 0 else float("nan")
        print(f"| {n}{nested} | {ms:.3f} | {100 * ms / f_mean:.1f} | {calls:.1f} | {per_call:.1f} |")
    print()

    def bucketed(title, key, edges):
        print(f"## Frame time by {title}")
        print()
        print(f"| {key} | frames | frame ms (mean) | bda ms | non-bda ms | physics ms | parts total |")
        print("|---|---|---|---|---|---|---|")
        for lo, hi in zip(edges, edges[1:] + [math.inf]):
            sel = [r for r in rows if lo <= r[key] < hi]
            if not sel:
                continue
            label = f"{lo:g}+" if hi == math.inf else f"{lo:g}-{hi - 1:g}"
            print(f"| {label} | {len(sel)} | {mean([r['frame_ms'] for r in sel]):.1f} | {mean([r['bda_ms'] for r in sel]):.1f} | "
                  f"{mean([r['non_bda_ms'] for r in sel]):.1f} | {mean([r['physics_ms'] for r in sel]):.1f} | {mean([r['parts_total'] for r in sel]):.0f} |")
        print()

    bucketed("missiles in flight", "missiles_in_flight", [0, 1, 11, 26, 51, 76, 101])
    bucketed("total part count", "parts_total", [0, 500, 1000, 1500, 2000, 3000, 4000])

    gc_rows = [r for r in rows if r["gc_count"] > 0]
    print("## GC and memory")
    print()
    print(f"- Frames with a garbage collection: {len(gc_rows)} ({sum(r['gc_count'] for r in gc_rows):.0f} collections, "
          f"one every {duration_s / max(len(gc_rows), 1):.1f} s of frames).")
    if gc_rows:
        others = [r["frame_ms"] for r in rows if r["gc_count"] == 0]
        print(f"- Frame time with a GC: mean {mean([r['frame_ms'] for r in gc_rows]):.1f} ms vs {mean(others):.1f} ms without "
              f"(the difference is roughly the GC pause).")
    heap = col("heap_mb")
    print(f"- Managed heap: {min(heap):.0f}-{max(heap):.0f} MB.")
    print()
    print("## Scene")
    print()
    for name in ["vessels_loaded", "parts_total", "parts_max_vessel", "missiles_in_flight", "bullets_active", "guard_wms"]:
        v = col(name)
        print(f"- {name}: mean {mean(v):.0f}, max {max(v):.0f}")


if __name__ == "__main__":
    main()
