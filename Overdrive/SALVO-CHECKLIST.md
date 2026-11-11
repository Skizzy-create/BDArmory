# BDArmory Overdrive: guard mode missile fire checklist

Branch `overdrive-salvo-fix`. Covers the "fires one missile (or one wave), then never fires again" fix, in serial and parallel fire. Work through the sections in order and note the result next to anything that fails.

Conventions:

- `<KSP>` is the KSP install folder. Log file: `<KSP>\KSP.log`.
- After every scenario, search the log for `Exception` and `NullReferenceException`.
- Install, back-up and rollback steps are the same as section A of `HOOKS-CHECKLIST.md`. The DLL to test is `D:\KerbalSpaceProgram\BDArmory-salvo\BDArmory\bin\Release\BDArmory.dll`.

## A. Turn on the "why not firing" log

1. Quit KSP.
2. Open `<KSP>\GameData\BDArmory\PluginData\settings.cfg`. In the `BDASettings` node, set `DEBUG_GUARD_FIRE = True`. Add the line if it isn't there yet; the new DLL adds it, set to `False`, the first time it saves.
3. Optional: also set `DEBUG_MISSILES = True` for the stock missile log.
4. Start KSP. The log lines look like this:

   `[BDArmory.MissileFire]: <vessel> guard fire (GuardScan): not firing - <reason> (<state>)`

   The channels are `GuardScan` (serial fire), `ParallelSalvo` and `PointDefence` (interceptors against incoming missiles). Each line is written when the reason changes, and repeated at most every 5 s.

   The reasons:

   | Reason | Meaning |
   |---|---|
   | `launch routine busy` | The launch flag is held. |
   | `missiles/target quota full` | Missiles/Tgt is reached for the target. |
   | `max missile targets engaged` | A new target would go over Max Missile Tgts. |
   | `no weapon for the target` | No weapon, or no unfired missile of the selected type. |
   | `no weapon in range/launch checks failed` | Range, DLZ, guard angle, terrain or boresight check failed. |
   | `no radar lock` | A radar-guided missile has no lock or lock slot. |
   | `fire authority hold` | Fire authority is set to Hold. |
   | `no incoming missiles` | Point defence has nothing to engage. |
   | `no interceptor can engage` | Point defence has no interceptor able to fire. |
   | `firing` | A launch started. |

   The `(...)` part lists every state that can stop fire:
   - the launch flag and its owner: free, a launch routine (missile, target, seconds since its last progress), or a guided modular missile;
   - missiles/target and queued launches at the current target, and engaged targets out of Max Missile Tgts;
   - the selected weapon, and whether its missile has already fired;
   - radar locks used out of capacity;
   - fire authority and the external override;
   - the incoming missile count, and how many interceptor failures are remembered.

## B. Main repro: serial fire, one missile then nothing

1. Spawn a ship with at least two SAM types, for example a VLS with short-range cells plus long-range cells (or rails). Turn on its radar. Keep `Parallel Fire` off and use the default `Missiles/Tgt` = 1 and `Max Missile Tgts` = 1.
2. Spawn 4+ hostile aircraft (or a wave of 20 jets carrying RB-15 or other anti-ship missiles) about 30 km out. Turn on guard mode on everything.
3. Expected:
   1. The ship fires one missile and waits while it flies (log: `missiles/target quota full`).
   2. When the missile hits, misses or its target dies, the ship fires again on the next scan.
   3. This continues until the jets are dead or the SAMs are empty. Fire never stops for good.
4. Empty the short-range type (let it fire, or use a craft with one short-range cell). Expected: guard mode switches to the type that still has missiles. The log must not repeat `no weapon for the target ... ready missile <empty type> (fired, no unfired one of this type)` for more than one scan.
5. While RB-15s are incoming and interceptors are flying, the ship still fires at the jets. Before this fix, one interceptor in flight counted as an engaged target and blocked every jet with Max Missile Tgts = 1.
6. Toggle guard mode off and on mid-fight. Expected: fire resumes on the next scan.
7. Modular (MMG) missiles, if you have any: fire one in guard mode. Expected: the launching ship keeps scanning and firing other missiles while the MMG guides. Its log must not show `launch flag held by a guided modular missile`. Before this fix, the first MMG froze guard fire on the ship for good. An earlier draft of the fix still froze it until the MMG hit. Only an MMG that carries its own weapon manager holds its own launch flag while it guides, so it doesn't re-target.

## C. Parallel fire

1. Same scenario as B, with `Parallel Fire` on, `Salvo Size` 2, `Missiles/Tgt` 2 and `Max Missile Tgts` 4.
2. Expected:
   1. Salvos go out every firing interval at up to 4 targets.
   2. Salvo size works with a single multi-cell VLS or pod. Each target gets min(Salvo Size, loaded cells, Missiles/Tgt) missiles in one ripple from that launcher. Before this fix it got one missile per launcher.
   3. With several launchers of the same type, the salvo is spread evenly: Salvo Size 6 across 3 launchers fires 2 from each.
   4. Missiles/Tgt still caps the salvo. With Missiles/Tgt = 1, a salvo is always one missile, so set Missiles/Tgt to at least the salvo size.
   5. When missiles miss, the next salvo tops those targets up.
   6. If no plan is formed, the log line `guard fire (ParallelSalvo): not firing - ...` lists the per-target counts (saturated, over Max Missile Tgts, no ready missile, launch checks failed, no radar lock, locks used/capacity).

## D. Point defence against many incoming missiles

1. Spawn a ship with interceptor missiles (`engageMissile` on) of two types, and 10+ hostile craft firing anti-ship missiles at it. Turn on guard mode.
2. Expected: interceptors keep launching while missiles are incoming. If one interceptor can't launch (blocked rail, no clearance, no lock), the log shows `point defence: <interceptor> didn't launch at <missile>, trying other interceptors first`, and the next launch uses another missile or type within about 0.1 s.
3. The `PD rejected target due to min dist` and `engagement envelope` lines now only appear with `DEBUG_APS` on. They used to spam the log every 0.1 s.

## E. Watchdog

The launch routines now always release the launch flag, including when they throw. If a routine is stopped without its clean-up, the watchdog releases the flag once the routine has made no progress for 30 s (or 3 x the firing interval if that is longer). Look for this warning:

`[BDArmory.MissileFire]: <vessel>'s missile launch routine made no progress for <n>s, releasing the launch flag.`

It should be rare. Report the lines before it, especially any `Exception`.

## F. Regression

1. Stock air battle (2 x 4 aircraft, guns and missiles), with `DEBUG_GUARD_FIRE` off. Missile use should look like the previous DLL, except that firing doesn't stall.
2. Bombers in guard mode still complete bombing runs. Bombing runs can last long, so check that the watchdog warning does not appear during normal runs.
3. With `DEBUG_GUARD_FIRE` off, the log has no `guard fire (` lines.

## G. Layered defence against incoming missiles (point defence)

Settings in `settings.cfg` (`BDASettings` node):
- `GUARD_LAYERED_DEFENCE = True` is the default. `False` restores the stock order: incoming missiles taken round-robin, shortest-range interceptor first.
- `GUARD_SHOOT_SHOOT_TTI = 0` is the default (off). Set it to a number of seconds, for example `12`, to fire two interceptors at once at missiles that close to impact, even with Missiles/Tgt = 1.

1. Spawn a fresh ship with three interceptor types: short (RAM or similar), medium (ESSM) and long (SM-2). Turn on its radar and guard mode. Spawn several craft that launch cruise or anti-ship missiles at it from 40+ km.
2. Expected with layered defence on, using `DEBUG_GUARD_FIRE`:
   1. The first interceptors are the long-range type, launched as soon as the incoming missiles come within its range. The log line `guard fire (PointDefence): firing - <missile> (range <min>-<max>m) at <target>, <distance>m, time to impact <t>s, layered` shows which type and when.
   2. Medium and short range follow for missiles that get closer. They are also used at once when the long-range type is empty, has no lock, or fails to launch, with no stall between types.
   3. The incoming missile with the lowest time to impact is served first, not just the nearest.
   4. When an interceptor misses, is destroyed, loses its seeker or self-destructs, that missile is engaged again on the next scan, within about 0.1 s.
3. Set `GUARD_LAYERED_DEFENCE = False`, restart, and repeat. Expected: stock behaviour. With threats already close, the shortest-range type fires first. Note the difference for the report.
4. Shoot-shoot: set `GUARD_SHOOT_SHOOT_TTI = 12`. Expected: missiles under 12 s from impact get two interceptors in flight at once. Others still get Missiles/Tgt.
5. Long fights with 50+ incoming missiles: no frame-rate drop compared with the setting off. Layered defence adds one pass over the interceptors and one over the incoming missiles per point defence scan (every 0.1 s), with no allocations.
