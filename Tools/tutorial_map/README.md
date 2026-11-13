# Tutorial Station generator

`Resources/Maps/tutorial.yml` is a single-player training map with one room per crew job.
It has 75 rooms: every crew job, two antagonist wings, an antagonist awareness wing for crew, and a salvage dock with a flyable shuttle. Every room has a reset button, windows onto the hallway, and a trim in its wing's colour. It is generated. Change the room data here and regenerate rather than editing the map in the
mapping editor, or your edits are lost the next time someone regenerates it.

| File | What it holds |
| --- | --- |
| `rooms.py` | Every room: title, jobs that spawn there, equipment, lesson papers and the practice task. |
| `generate.py` | Geometry: corridor, wings, walls, doors, lights, power cables, portals. Writes a layout JSON. |
| `render.py` | Draws an overhead plan as a PNG, for checking layout changes. |
| `Content.IntegrationTests/Tests/Tutorial/TutorialMapBuilder.cs` | Spawns the layout through the real engine, fills the air and saves the map. Explicit: it never runs in CI. |
| `Content.IntegrationTests/Tests/Tutorial/TutorialMapTest.cs` | Regular tests: power, gravity, air and portal pairs; the salvage shuttle docks at round start; a room reset restores papers, floor and removes junk. |
| `Resources/Prototypes/Maps/tutorial.yml` | The `Tutorial` game map: every job, unlimited slots. Not in any map pool. |
| `Resources/Prototypes/Entities/Effects/tutorial_portals.yml` | Colour-coded portal gateways, one colour per wing. |
| `Content.Shared/Tutorial/TutorialRoomResetComponent.cs`, `Content.Server/Tutorial/TutorialRoomResetSystem.cs` | The room reset button. It snapshots its room on the first tick after map init, and on use deletes and respawns everything in the room except players, ghosts, portals and linked machines. |
| `Resources/Prototypes/Entities/Structures/Wallmounts/tutorial_reset_button.yml` | The reset button prototype. The generator sets each button's room bounds. |
| `Resources/Prototypes/Entities/Structures/Doors/Airlocks/tutorial.yml` | The salvage dock airlock. Its `GridFill` docks `/Maps/Shuttles/mining.yml` at round start (only when `shuttle.grid_fill` is on, as it is in normal play). |

## Regenerating

```bash
python3 Tools/tutorial_map/generate.py /tmp/tutorial_layout.json
```

```bash
TUTORIAL_LAYOUT=/tmp/tutorial_layout.json TUTORIAL_OUTPUT=$PWD/Resources/Maps/tutorial.yml dotnet test Content.IntegrationTests --filter "FullyQualifiedName~TutorialMapBuilder"
```

```bash
dotnet test Content.IntegrationTests --filter "FullyQualifiedName~TutorialMapTest|FullyQualifiedName~PostMapInitTest&Name~utorial"
```

The builder lists every unknown entity or tile id before it spawns anything, so a renamed prototype fails fast with its name.

## Playing it

Start a local server, open the console and run `forcemap Tutorial`, then `restartround`.
Set the job you want as your top priority and ready up. To change job mid-round, run `respawn` and late-join as any job.
Late joiners arrive in the Hub. The Portal Hall, through the Hub's west door, has a portal to every room.

## Antagonists

A one-player server never rolls antagonists, so the antagonist rooms teach each role and tell you to make yourself one:
right-click your character, **Antag ctrl**, then **Make Traitor** (and so on). On a local server you are host admin.

## Adding a room

Add a dict to a wing list in `rooms.py` and a matching entry in `TASKS`. Wings hold any number of rooms; they grow away
from the main corridor two rooms per row. Items in `wall` line the walls facing inward, `table` items go on centre
tables, `floor_items` go on free floor and `place` puts things at exact `(u, v)` spots, where `u` is the distance from
the hallway wall (0 to 8) and `v` runs along it (0 to 6).
