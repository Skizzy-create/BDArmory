BDArmory Overdrive
==================

Gun turrets and other weapon systems for KSP, built on top of BDArmory Plus 1.13.1, with bug
fixes and improvements.

BDArmory Overdrive is a fork of [BDArmory Plus](https://github.com/BrettRyland/BDArmory) by
[Skizzy-create](https://github.com/Skizzy-create). It is a drop-in replacement: it installs to
`GameData/BDArmory`, and the plugin (`BDArmory.dll`), namespaces, part modules, config node
names and settings are the same as BDArmory Plus 1.13.1. Weapon packs, craft and saves made
for BDArmory Plus keep working. Install one BDArmory only.

This is an unofficial build. Please report problems with it
[here](https://github.com/Skizzy-create/BDArmory/issues), not to the BDArmory Plus team.

Download
========

- **CKAN**: BDArmory Overdrive (`BDArmory-Overdrive`) is listed in the SkizzyNaval metadata
  repository (not yet in the main CKAN index). In CKAN, open *Settings* > *Metadata
  repositories* > *New*, enter the name `SkizzyNaval` and the URL
  `https://github.com/Skizzy-create/skizzy-ckan/archive/refs/heads/main.tar.gz`, keep
  `KSP-default`, refresh, and install "BDArmory Overdrive". It provides `BDArmory` and
  `BDArmoryForRunwayProject`, so mods that depend on BDArmory install with it. If you have
  `BDArmory-Skizzy`, switch with `ckan update`, `ckan upgrade --all` (picks up its
  `replaced_by`), then `ckan replace BDArmory-Skizzy` (or tick *Replace* in the GUI).
- **Manual**: download `BDArmoryOverdrive-<version>.zip` from
  [Releases](https://github.com/Skizzy-create/BDArmory/releases) and unzip it into the KSP
  folder, so that you get `GameData/BDArmory`. Remove any other BDArmory first. It needs
  [ModuleManager](https://github.com/sarbian/ModuleManager) and
  [PhysicsRangeExtender](https://github.com/jrodrigv/PhysicsRangeExtender), like BDArmory Plus.

KSP 1.12.x (built and checked against 1.12.5).

What's different from BDArmory Plus 1.13.1
==========================================

- **Multi-target missile engagement.** The weapon manager can engage up to 1024 targets, with up
  to 128 missiles per target (BDArmory Plus: 18). Fixes the 2-target cap and an off-by-one in
  guard mode's launch authorization (it allowed one target more than "Max Missile Tgts"). Radar
  lock slots can follow the multi-target setting.
- **Retargeting.** Heat, radar, GPS-datalink and inertial missiles whose target is destroyed
  retarget to the nearest engageable hostile (setting `MISSILE_RETARGETING`).
- **Parallel salvo.** New weapon manager option "Fire In Parallel": each firing interval, guard
  mode fires a salvo of up to "Salvo Size/Tgt" missiles at the current and secondary targets,
  spread over rails, tubes and cells, with a configurable stagger. Off by default, so guard mode
  behaves as before unless you enable it.
- **Depth charges.** Guard mode releases unpowered depth charges and unguided drop weapons over
  submerged targets, predicting where the charge sinks to its fuze depth, and only when the
  blast clears its own ship and friendlies.
- **MultiMissileLauncher fixes.** Loop bounds, inverted engagement checks, heat-seeker aim,
  scan sizing, reload jams and queued-launch timeouts.
- **Memory-leak fixes for long battles.** Destroyed vessels, mesh copies and material copies are
  released during the flight scene instead of piling up until the scene changes; static event
  subscriptions no longer stack. GameEvents callbacks leaked by PhysicsRangeExtender 1.21.0 are
  removed on each scene start.
- **Debris clean-up delay slider** goes from 1 s to 900 s (it was 1 to 60 s, and opening the
  settings window reset a longer delay set in `settings.cfg` to 60 s).
- The English settings window title reads "BDArmory Overdrive Settings".

Full details are in the commit messages on the
[`multitarget-1.13.1`](https://github.com/Skizzy-create/BDArmory/commits/multitarget-1.13.1)
branch and in the release notes.

Building
========

Open `BDArmory/BDArmory.csproj` and build Release. The references are expected in
`_LocalDev/KSPRefs` next to the repository folder (a copy of KSP's `KSP_x64_Data/Managed`). Release zips are built with
`-p:DebugType=None -p:DebugSymbols=false`, so the DLL carries no local paths.
`Overdrive/make_release.py` assembles the zip from the BDArmory Plus 1.13.1.0 release, the
built DLL and the changed files from `BDArmory/Distribution`.

Credits
=======

BDArmory Overdrive exists because of the people who wrote and maintained BDArmory:

- **Original author:** [BahamutoD](https://github.com/BahamutoD) (Paolo Encarnacion), BDArmory.
- **BDArmory Continued:** maintained by [Papa_Joe](https://github.com/PapaJoesSoup) (Joe Korinek)
  in BahamutoD's absence, with [jrodrigv](https://github.com/jrodrigv),
  [SpannerMonkey](https://github.com/SpannerMonkey), [gomker](https://github.com/gomker),
  [TheDog](https://github.com/TheDogKSP) and [DoctorDavinci](https://github.com/DoctorDavinci).
- **BDArmory Plus team:** [DocNappers](https://github.com/BrettRyland) (BrettRyland),
  [SuicidalInsanity](https://github.com/SuicidalInsanity), [Josue](https://github.com/josuenos),
  [Bill Nye](https://github.com/BillNyeTheIE) and [Gedas-S](https://github.com/Gedas-S).
- **Contributors** (from the BDArmory Plus README and history): [ferram4](https://github.com/ferram4),
  [Aubranium](https://github.com/agoodman), [Halban](https://github.com/Halbann),
  [Spartwo](https://github.com/Spartwo), [Scott Manley](https://github.com/illectro),
  [Stardust](https://github.com/Stardust-Rapture), [Kurgan](https://github.com/TheKurgan),
  [Cl0by](https://github.com/Cl0by), Kaz, CeruleanEyes, Fluffy, Concodroid, EzBro and many more.
  See `GameData/BDArmory/Credits.txt` for asset and code credits (sounds, Firespitter texture
  switching, reloadable rails by flywlyx, and others).
- **Overdrive changes:** [Skizzy-create](https://github.com/Skizzy-create).

Forum threads: [BDArmory Plus](https://forum.kerbalspaceprogram.com/index.php?/topic/209092-19x-112x-bdarmory-plus-bda-2022-07-23/),
[BDArmory Continued](https://forum.kerbalspaceprogram.com/index.php?/topic/184167-17x-bdarmory-continued-v130-05012019/),
[original BDArmory](http://forum.kerbalspaceprogram.com/threads/85209-BDArmory).

Licensing
=========

This mod for Kerbal Space Program was originally developed by Paolo Encarnacion (BahamutoD) and
distributed under the license CC-BY-SA 2.0. It was then maintained in BahamutoD's absence by Joe
Korinek (Papa_Joe) and continues to be distributed under the license CC-BY-SA 2.0. BDArmory
Overdrive's changes are distributed under the same license, CC-BY-SA 2.0.

**Credit.** CC-BY-SA 2.0 requires attribution. When you redistribute BDArmory Overdrive or a work
based on it, credit: "BDArmory by Paolo Encarnacion (BahamutoD), maintained by Joe Korinek
(Papa_Joe) and the BDArmory Continued team; Overdrive changes by Skizzy-create"
(https://github.com/Skizzy-create/BDArmory).
Please read about the license at https://creativecommons.org/licenses/by-sa/2.0/ before
attempting to modify and redistribute it. The full notice, including the Firespitter
permission notice, is in `BDArmory/Distribution/GameData/BDArmory/License.txt` (shipped as
`GameData/BDArmory/License.txt`).
