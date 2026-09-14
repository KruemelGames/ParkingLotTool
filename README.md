# Parking Lot Tool

A Cities: Skylines II mod that generates a complete, working parking lot inside
any polygon you draw — bays, aisles, connecting roads, a perimeter road,
markings and surfaces, with cars that actually park.

## Status

Work in progress, and honest about it: it builds working parking lots, but it
is not on the Workshop yet and things still break.

## Reporting a problem

Open an [issue](https://github.com/KruemelGames/ParkingLotTool/issues). That is
the only channel — it keeps the file, the description and the follow-up
questions in one place, and other people can see what is already known.

Before you write it, use the **Report a problem** tab in the panel. It writes a
single `.zip` into your Cities: Skylines II logs folder containing everything
needed to look into it, including a crash report if the game died last time.
Drag that file into the issue.

Then say, briefly:

- what you did, step by step — drew an area, built, edited, bulldozed
- what you expected, and what happened instead
- whether the game crashed
- which other mods were running

Everything technical is already in the report file. Please do not paste logs.

**One thing to know before you upload.** A crash report includes the game's own
`Player.log`, and that file contains your Windows user name, your Steam ID, the
names of your save games and the list of your installed mods. Issues are
public. If that bothers you, open the `.zip` and remove `Player.log` and
`Player-prev.log` — the report is still useful without them, just less so for
crashes.

## What it does

Draw a polygon. The mod computes a layout inside it and builds it as real
game objects:

- **Parking bays** as decals that carry a working `ParkingLane` — cars really
  park there, it is not decoration.
- **Aisles, connecting roads and a perimeter road** as invisible paths, so the
  lot is drivable and connects to the road you drew next to.
- **Two surfaces** — one for the driving area, one for everything in between —
  both freely selectable from the game's surfaces, or switched off entirely if
  you would rather paint the ground yourself.
- **Entrances** you place by hand, snapped to an existing road.

The whole lot has a single owner, so it can be selected and bulldozed as one
thing.

## Requirements

These are **hard dependencies**. Without them the mod will not work — install
them before you enable it.

| Mod | Why it is needed |
|---|---|
| [Unified Icon Library](https://mods.paradoxplaza.com/mods/74417/Windows) | every icon in the panel comes from it |
| [Asset Icon Library](https://mods.paradoxplaza.com/mods/79634/Windows) | the surface pictures in the surface picker |

Plus Cities: Skylines II itself, of course.

If the panel opens but icons show as white boxes, one of the two is missing.

**Harmony does not need installing.** The mod ships its own copy of
`0Harmony.dll` and it lands in the mod folder on build. It is used to read the
game's own selection, bulldoze and upkeep behaviour. Licence and origin are in
the header of `Tools/ParkingLotRaycastPatch.cs`.

## Building

Needs the official CS2 modding toolchain (`CSII_TOOLPATH`, `CSII_MANAGEDPATH`
and `CSII_USERDATAPATH` set by its installer).

```
dotnet build -c Release
```

The build also compiles the React UI and deploys everything to the local mods
folder. `-c Debug` builds for Windows only and is roughly twice as fast.

## Layout of the repository

| Folder | What is in it |
|---|---|
| `Geometry/` | the layout computation — no game types, so it runs headless |
| `Geometry/Zellen/` | the current engine: convex decomposition, row frame, cells |
| `Tools/` | everything that talks to the game: ECS systems, placement, snapping |
| `UI/src/` | the panel, React and SCSS, bundled by webpack |
| `Tests/GeometryParity/` | the measuring harness — see below |

## About the tests

The geometry is deliberately free of game types so it can be measured without
starting Cities: Skylines II. `Tests/GeometryParity/CSharp` is less a unit test
suite than a set of **measuring instruments**:

```
dotnet run -c Release                        # the reference cases
dotnet run -c Release -- --flaechen          # what the game will accept, over
                                             # every polygon ever drawn
dotnet run -c Release -- --triangulierung    # does our model of the game's
                                             # triangulation match the game?
dotnet run -c Release -- --lformen           # 1152 shapes with reflex corners
dotnet run -c Release -- --qualitaet         # the quality run
```

`--triangulierung` is worth a word. Cities: Skylines II silently discards a
surface whose triangulation fails, and for a long time this project guessed at
the rule. It is now a line-by-line port of the game's own ear clipping,
checked against measurements taken inside the running game — three independent
binary searches landing on the same interval. Where the mod used to guess, it
now computes what the game computes.

## A note on how this was written

This mod was written with AI assistance, and I want to be upfront about it
rather than have someone find out.

That is not the same as letting a model improvise code until it compiles. The
way this repository is built, you can check that for yourself:

- Comments state **measured numbers and the run they came from**, not
  intentions. Where something is a guess, it says so.
- Several findings are backed by decompiling the game and quoting the actual
  code, rather than by assumption.
- Wrong turns are written down next to the fix — including the ones that cost
  days — so nobody repeats them.
- There is a harness that measures the geometry against every shape a real user
  has ever drawn.

If you think something in here is wrong, the fastest way to show it is to run
the harness and post the numbers.

## Licence

[GPL-3.0](LICENSE).

Copyright (c) 2026 KruemelGames.

You may use, study, change and share this — but anything you publish that is
built on it has to be free software under the same licence, with its source
available. Whoever takes, gives back.

Contributions are welcome and are taken to be under the same licence.

### Third-party code

**Shipped with the mod:** [Harmony](https://github.com/pardeike/Harmony) 2.2.2
(`0Harmony.dll`), Copyright (c) 2017 Andreas Pardeike, MIT licence. The binary
is unmodified, taken from the [Lib.Harmony 2.2.2](https://www.nuget.org/packages/Lib.Harmony/2.2.2)
NuGet package; no Harmony source code was copied into this project. Its licence
text travels with it — in the repository at `Library/Harmony/LICENSE`, and in
the built mod folder as `0Harmony-LICENSE.txt`. MIT is compatible with GPL-3.0.

**Not shipped, required at runtime:**
[Unified Icon Library](https://github.com/algernon-A/UnifiedIconLibrary)
(Apache-2.0) and Asset Icon Library. None of their code is included here; the
player installs them separately. Apache-2.0 is one-way compatible with GPL-3.0,
so that combination is fine.
