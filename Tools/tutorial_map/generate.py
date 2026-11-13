#!/usr/bin/env python3
"""
Generates the layout JSON for the Tutorial Station.

  python3 Tools/tutorial_map/generate.py layout.json [summary.md]

The JSON is turned into Resources/Maps/tutorial.yml by the explicit integration
test Content.IntegrationTests/Tests/Tutorial/TutorialMapBuilder.cs, which spawns
everything through the real engine and saves the map. See Tools/tutorial_map/README.md.

Geometry, in grid tiles:
  * a main corridor along y = -1..1, from the Hub (west) to the Power Core (east)
  * one wing per department, north (y > 0) or south (y < 0) of the corridor
  * each wing is a 3-wide hallway at x = cx-1..cx+1 with 9x7 rooms on both sides
"""
import json
import math
import sys

from rooms import WINGS, HUB_PAPERS, TASKS

ROOM_W, ROOM_H = 9, 7     # room interior size: u (away from the hallway) x v (along it)
WING_SPACING = 24          # distance between wing centres
FIRST_WING_X = 14
STATION_ID = "Tutorial"

PI = math.pi
FACING_ROT = {"S": 0.0, "E": PI / 2, "N": PI, "W": -PI / 2}

tiles = {}       # (x, y) -> tile id, floors only (walls get plating later)
indoor = set()   # floor cells inside the airtight hull
outdoor = set()  # floor cells outside (EVA catwalk)
ents = []
lv_skip = set()  # cells kept off the station's LV network
mv = set()
hv = set()


def ent(proto, x, y, facing=None, rot=0.0, **extra):
    e = {"p": proto, "x": x + 0.5, "y": y + 0.5, "r": FACING_ROT[facing] if facing else rot}
    for k, v in extra.items():
        if v is not None:
            e[k] = v
    ents.append(e)
    return e


def floor(x, y, tile):
    tiles[(x, y)] = tile
    indoor.add((x, y))


def rect(x0, y0, x1, y1, tile):
    for x in range(x0, x1 + 1):
        for y in range(y0, y1 + 1):
            floor(x, y, tile)


def paper(x, y, text, name=None):
    ent("Paper", x, y, paper=text, name=name)


# ------------------------------------------------------------------ rooms
class Room:
    """Maps a room's local (u, v) to world (x, y). u = 0 touches the hallway wall."""

    def __init__(self, cx, side, west, row):
        self.west = west
        self.hall_wall_x = cx - 2 if west else cx + 2
        self.y0 = 3 + 8 * row if side == "N" else -9 - 8 * row

    def xy(self, u, v):
        x = self.hall_wall_x - 1 - u if self.west else self.hall_wall_x + 1 + u
        return x, self.y0 + v

    def facing(self, f):
        away = "W" if self.west else "E"
        toward = "E" if self.west else "W"
        return {"in": toward, "out": away, "up": "N", "down": "S", None: None}[f]


WALL_SLOTS = (
    [(8, v, "in") for v in (3, 1, 5, 2, 4, 0, 6)]
    + [(u, 6, "down") for u in (7, 5, 3, 6, 4, 2)]
    + [(u, 0, "up") for u in (7, 5, 3, 6, 4, 2)]
)
TABLE_SLOTS = [(4, 3), (5, 3), (4, 2), (5, 2), (4, 4), (5, 4)]
FLOOR_SLOTS = [(6, 1), (6, 5), (3, 1), (3, 5), (7, 3), (2, 2), (2, 4), (7, 1), (7, 5), (3, 3)]
LESSON_TABLE = [(0, 6), (1, 6)]
SPAWN_SLOTS = [(1, 4), (1, 2), (2, 3)]

MINING_ROCKS = [(u, v) for u in range(2, 9) for v in range(0, 7)
                if not (v == 3 or (u, v) in TABLE_SLOTS or u == 2 and v in (2, 4))]

summary = []
room_rects = []  # (wing, title, [(x, y), (x, y)]) for render.py
room_portals = []  # (wing index, room title, portal key) for the Portal Hall

COLOUR_WORD = {"Basics": "white", "Command": "blue", "Security": "red", "Engineering": "orange", "Science": "purple",
               "Medical": "cyan", "Cargo": "brown", "Food": "green", "Culture": "pink", "Civilian": "grey"}

PORTAL_COLOUR = {
    "Basics Academy": "Basics", "Command": "Command", "Security": "Security", "Engineering": "Engineering",
    "Science": "Science", "Medical": "Medical", "Cargo": "Cargo", "Service: Food": "Food",
    "Service: Culture": "Culture", "Civilian": "Civilian",
}


def build_room(spec, cx, side, west, row, wing_name, wing_index=0):
    r = Room(cx, side, west, row)
    title = spec["title"]
    assert title in TASKS, f"No practice task for {title}"
    summary.append((wing_name, title, spec.get("jobs", []), spec))
    corners = [r.xy(0, 0), r.xy(ROOM_W - 1, ROOM_H - 1)]
    room_rects.append((wing_name, title, corners))

    # floor
    for u in range(ROOM_W):
        for v in range(ROOM_H):
            x, y = r.xy(u, v)
            floor(x, y, spec.get("tiles", {}).get((u, v), spec["floor"]))
            if spec.get("nopower"):
                lv_skip.add((x, y))

    used = {(0, 0)}

    # portal back to the Portal Hall, in the corner beside the door
    key = f"room{len(room_portals)}"
    px_, py_ = r.xy(0, 0)
    ent(f"PortalGatewayTutorial{PORTAL_COLOUR[wing_name]}", px_, py_, key=key, name="to Portal Hall")
    room_portals.append((wing_index, title, key))

    # door into the hallway, at v = 3
    dx, dy = r.xy(-1, 3)
    floor(dx, dy, "FloorSteel")
    ent(spec.get("door", "AirlockGlass"), dx, dy)

    # APC on the hallway wall, fed by an MV spur from the hallway centre line
    ax, ay = r.xy(-1, 5)
    if spec.get("nopower"):
        # Electrical Workshop: the practice APC sits on the far wall, off the station grid,
        # with a stub of MV and LV under it for the student to connect to.
        px, py = r.xy(9, 5)
        ent("APCBasic", px, py, r.facing("in"))
        ent("CableMV", px, py)
        ent("CableApcExtension", px, py)
    else:
        ent("APCBasic", ax, ay, r.facing("out"))
        for x in range(min(ax, cx), max(ax, cx) + 1):
            mv.add((x, ay))
        tiles_lv_extra.add((ax, ay))

    # lights
    light = "AlwaysPoweredWallLight" if spec.get("nopower") else "Poweredlight"
    for u, v, f in ((4, 6, "down"), (4, 0, "up")):
        x, y = r.xy(u, v)
        ent(light, x, y, r.facing(f))

    # lesson table and papers
    for u, v in LESSON_TABLE:
        x, y = r.xy(u, v)
        ent("Table", x, y)
        used.add((u, v))
    for i, text in enumerate(spec.get("papers", [])):
        u, v = LESSON_TABLE[i % 2]
        x, y = r.xy(u, v)
        header = f"[italic]{wing_name} · {title} · lesson {i + 1} of {len(spec['papers'])}[/italic]\n\n"
        if i == 0 and title in TASKS:
            text += f"\n\n[bold]Practice task:[/bold] {TASKS[title]}"
        paper(x, y, header + text, name=f"lesson {i + 1}: {title}")

    # spawn points
    for i, job in enumerate(spec.get("jobs", [])):
        if job == "StationAi":
            continue  # PlayerStationAi in `place` is the AI's spawn point
        u, v = SPAWN_SLOTS[i]
        x, y = r.xy(u, v)
        ent("SpawnPointAtmos" if job == "AtmosphericTechnician" else f"SpawnPoint{job}", x, y)

    # explicit placements
    for item in spec.get("place", []):
        proto, u, v, f = item[:4]
        key = item[4] if len(item) > 4 else None
        link = item[5] if len(item) > 5 else None
        x, y = r.xy(u, v)
        name = "workshop target light" if spec.get("nopower") and proto == "Poweredlight" else None
        ent(proto, x, y, r.facing(f), key=key, linkTo=link, name=name)
        used.add((u, v))

    if spec.get("cell"):
        # practice cell: an inner wall at u = 6 with a door at v = 3
        for v in range(ROOM_H):
            x, y = r.xy(6, v)
            ent("AirlockSecurityGlass" if v == 3 else "WallSolid", x, y)
            used.add((6, v))

    if spec.get("rocks"):
        for u, v in MINING_ROCKS:
            if (u, v) in used:
                continue
            x, y = r.xy(u, v)
            ent("AsteroidRockMining", x, y)
            used.add((u, v))

    # wall-side machines
    slots = [s for s in WALL_SLOTS if (s[0], s[1]) not in used]
    for proto in spec.get("wall", []):
        u, v, f = slots.pop(0)
        x, y = r.xy(u, v)
        wallmount = proto in WALLMOUNTS
        if wallmount:
            # mounted on the wall tile itself
            du, dv = {"in": (1, 0), "down": (0, 1), "up": (0, -1)}[f]
            x, y = r.xy(u + du, v + dv)
        ent(proto, x, y, r.facing(f))
        if not wallmount:
            used.add((u, v))

    # tables and table items
    items = spec.get("table", [])
    if items:
        n_tables = min(len(TABLE_SLOTS), max(2, math.ceil(len(items) / 2)))
        table_slots = [s for s in TABLE_SLOTS if s not in used][:n_tables]
        for u, v in table_slots:
            x, y = r.xy(u, v)
            ent("Table", x, y)
            used.add((u, v))
        for i, proto in enumerate(items):
            u, v = table_slots[i % len(table_slots)]
            x, y = r.xy(u, v)
            ent(proto, x, y)

    free = [s for s in FLOOR_SLOTS if s not in used]
    for proto in spec.get("floor_items", []):
        u, v = free.pop(0)
        x, y = r.xy(u, v)
        ent(proto, x, y)

    if spec.get("eva"):
        build_eva(r)


WALLMOUNTS = {"AirAlarm", "FireAlarm", "ExtinguisherCabinetFilled", "IntercomCommon"}


def build_eva(r):
    """External airlock pair in the far wall, leading to a catwalk in space."""
    ix, iy = r.xy(9, 3)       # inner airlock, in the far wall
    cx_, cy_ = r.xy(10, 3)    # one-tile chamber
    ox, oy = r.xy(11, 3)      # outer airlock
    for x, y in ((ix, iy), (cx_, cy_), (ox, oy)):
        floor(x, y, "FloorSteel")
    ent("AirlockExternalGlass", ix, iy)
    ent("AirlockExternalGlass", ox, oy)
    for du in range(12, 18):
        for dv in (2, 3, 4):
            x, y = r.xy(du, dv)
            tiles[(x, y)] = "Lattice"
            outdoor.add((x, y))
            ent("Catwalk", x, y)
    for du in range(12, 18):
        for dv in (1, 5):
            x, y = r.xy(du, dv)
            tiles[(x, y)] = "Lattice"
            outdoor.add((x, y))
            ent("Grille", x, y)


tiles_lv_extra = set()


# ------------------------------------------------------------------ wings
def build_wing(i, wing):
    cx = FIRST_WING_X + WING_SPACING * (i % 5)
    side = wing["side"]
    rooms = wing["rooms"]
    rows = math.ceil(len(rooms) / 2)
    sgn = 1 if side == "N" else -1
    hall_len = 8 * rows - 1  # tiles beyond the corridor wall
    # hallway: from the corridor wall row (y = ±2) outwards
    for k in range(hall_len + 1):
        y = sgn * (2 + k)
        for x in (cx - 1, cx, cx + 1):
            floor(x, y, "FloorSteel")
            mv.add((cx, y))
    # glass airlocks where the hallway meets the corridor
    for x in (cx - 1, cx, cx + 1):
        ent("AirlockGlass", x, sgn * 2)
    # hallway lights on the east wall, facing west
    for k in range(3, hall_len + 1, 4):
        ent("Poweredlight", cx + 1, sgn * (2 + k), "W")
    # department sign beside the hallway entrance, on the corridor wall
    if wing["sign"]:
        ent(wing["sign"], cx - 2, sgn * 2, "S" if side == "N" else "N")
    for n, spec in enumerate(rooms):
        build_room(spec, cx, side, west=(n % 2 == 0), row=n // 2, wing_name=wing["name"], wing_index=i)


def build():
    # main corridor
    corridor_end = FIRST_WING_X + WING_SPACING * 4 + 12
    rect(2, -1, corridor_end, 1, "FloorSteel")
    for x in range(-11, corridor_end + 1):
        mv.add((x, 0))
    for x in range(6, corridor_end, 8):
        ent("Poweredlight", x, 1, "S")
        ent("Poweredlight", x + 4, -1, "N")

    # hub
    rect(-11, -5, 1, 5, "FloorDark")
    for x, y in ((-9, 2), (-9, -2), (-7, 2), (-7, -2)):
        ent("SpawnPointLatejoin", x, y)
    ent("SpawnPointObserver", -8, 0)
    for x in (-10, -9):
        ent("Table", x, 4)
    for i, text in enumerate(HUB_PAPERS):
        paper(-10 + i % 2, 4, text, name=["welcome", "switching jobs", "getting help"][i])
    ent("VendingMachineClothing", -11, -5, "N")
    ent("VendingMachineCola", -10, -5, "N")
    ent("VendingMachineSnack", -9, -5, "N")
    for x in (-4, -3, -2):
        ent("ChairOfficeLight", x, 4, "S")
    ent("Poweredlight", -6, 5, "S")
    ent("Poweredlight", -6, -5, "N")
    ent("Poweredlight", -11, 0, "E")
    ent("APCBasic", 0, 6, "S")
    for y in range(0, 6):
        mv.add((0, y))
    tiles_lv_extra.add((0, 6))
    # directory papers: one per wing
    directory = "[head=2]Station directory[/head]\n\nWings, west to east along the main corridor:\n\n"
    for i, w in enumerate(WINGS):
        directory += f"{'North' if w['side'] == 'N' else 'South'} {i % 5 + 1}: [bold]{w['name']}[/bold]\n"
    directory += "\nEast end: Station Power Core."
    paper(-9, 4, directory, name="station directory")

    # power core at the east end
    core_x0 = corridor_end + 2
    rect(corridor_end + 1, 0, corridor_end + 1, 0, "FloorSteel")
    ent("AirlockEngineeringGlass", corridor_end + 1, 0)
    rect(core_x0, -4, core_x0 + 8, 4, "FloorDarkMono")
    for x in range(core_x0, core_x0 + 9):
        for y in range(-4, 5):
            hv.add((x, y))
    for x in range(corridor_end, core_x0 + 9):
        mv.add((x, 0))
    for x in range(core_x0 + 3, core_x0 + 9):
        mv.add((x, -1))
    for i in range(12):
        ent("GeneratorRTG", core_x0 + 3 + i % 6, 2 + (i // 6) * 2 - 0)
    ent("SubstationBasic", core_x0 + 4, -1)
    ent("SubstationBasic", core_x0 + 6, -1)
    ent("GravityGenerator", core_x0 + 4, -3)
    ent("ComputerPowerMonitoring", core_x0 + 8, 0, "W")
    ent("APCBasic", core_x0 + 1, 5, "S")
    for y in range(0, 6):
        mv.add((core_x0 + 1, y))
    tiles_lv_extra.add((core_x0 + 1, 5))
    ent("Poweredlight", core_x0, 4, "S")
    ent("Poweredlight", core_x0 + 8, -4, "N")
    ent("Table", core_x0, -4)
    paper(core_x0, -4,
          "[head=2]Station Power Core[/head]\n\nTwelve RTGs (10 kW each) feed the HV network under this floor. "
          "Two substations step it down to MV, which runs along the main corridor and every hallway to each room's APC.\n\n"
          "The gravity generator keeps everyone's feet on the floor.\n\n"
          "Look, but do not unanchor anything here: the whole tutorial runs on this room. "
          "To practise wiring, use the Electrical Workshop in the Engineering wing.",
          name="power core notes")

    for i, wing in enumerate(WINGS):
        build_wing(i, wing)

    build_portal_hall(core_x0)


def build_portal_hall(core_x0):
    """West of the hub: one row per wing, one colour-coded portal per room, each linked to that room."""
    rect(-31, -11, -13, 11, "FloorDark")
    floor(-12, 0, "FloorDark")
    ent("AirlockGlass", -12, 0)
    for x in range(-31, -11):
        mv.add((x, 0))
    for y in range(0, 13):
        mv.add((-22, y))
    ent("APCBasic", -22, 12, "S")
    tiles_lv_extra.add((-22, 12))
    for x in (-27, -19):
        ent("Poweredlight", x, 11, "S")
        ent("Poweredlight", x, -11, "N")
    ent("Poweredlight", -31, 0, "E")

    for i, wing in enumerate(WINGS):
        y = 9 - 2 * i
        ent("Table", -31, y)
        paper(-31, y, f"[head=2]{wing['name']}[/head]\n\nThe {COLOUR_WORD[PORTAL_COLOUR[wing['name']]]}-coded portals in this row "
                      f"lead to the rooms of the {wing['name']} wing, in the same order as the wing's hallway. "
                      "Every room has a portal in the corner by its door that brings you back here.",
              name=f"{wing['name']} portals")
        rooms_here = [p for p in room_portals if p[0] == i]
        for k, (_, title, key) in enumerate(rooms_here):
            ent(f"PortalGatewayTutorial{PORTAL_COLOUR[wing['name']]}", -29 + 2 * k, y, portal=key, name=f"to {title}")

    # top row: the hub and the power core
    ent("PortalGatewayTutorialHub", -29, 11, key="core-hall", name="to Power Core")
    ent("PortalGatewayTutorialHub", core_x0 + 1, -3, portal="core-hall", name="to Portal Hall")
    ent("Table", -31, 11)
    paper(-31, 11, "[head=2]Portal Hall[/head]\n\nWalk into a portal to travel. Each portal's name (Shift + click to examine) says where it goes, "
                   "and its colour matches the wing:\n\n" + "\n".join(
                       f"[bold]{COLOUR_WORD[PORTAL_COLOUR[w['name']]]}[/bold]: {w['name']}" for w in WINGS) +
                   "\n[bold]gold[/bold]: Station Power Core\n\nThe hub is through the door to the east.",
          name="portal hall guide")


def finish():
    # walls around every indoor cell
    walls = set()
    for (x, y) in indoor:
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                c = (x + dx, y + dy)
                if c not in indoor and c not in outdoor:
                    walls.add(c)
    for (x, y) in sorted(walls):
        tiles[(x, y)] = "Plating"
        exterior = any((x + dx, y + dy) not in indoor and (x + dx, y + dy) not in walls
                       for dx in (-1, 0, 1) for dy in (-1, 0, 1))
        ent("WallReinforced" if exterior else "WallSolid", x, y)

    # cables. HV and MV only where planned; LV under every indoor floor + APC tiles.
    for (x, y) in sorted(hv):
        ent("CableHV", x, y)
    for (x, y) in sorted(mv):
        ent("CableMV", x, y)
    for (x, y) in sorted((indoor - lv_skip) | tiles_lv_extra):
        ent("CableApcExtension", x, y)


def write_summary(path):
    lines = ["| Wing | Room | Jobs that spawn here | Lessons |", "| --- | --- | --- | --- |"]
    for wing, title, jobs, spec in summary:
        lines.append(f"| {wing} | {title} | {', '.join(jobs) or '-'} | {len(spec.get('papers', []))} |")
    with open(path, "w") as f:
        f.write("\n".join(lines) + "\n")


if __name__ == "__main__":
    build()
    finish()
    out = {
        "stationId": STATION_ID,
        "gridName": "Tutorial Station",
        "tiles": [[x, y, t] for (x, y), t in sorted(tiles.items())],
        "ents": ents,
    }
    with open(sys.argv[1], "w") as f:
        json.dump(out, f)
    if len(sys.argv) > 2:
        write_summary(sys.argv[2])
    xs = [x for x, _ in tiles]
    ys = [y for _, y in tiles]
    print(f"{len(tiles)} tiles, {len(ents)} entities, {len(summary)} rooms, "
          f"bounds x {min(xs)}..{max(xs)} y {min(ys)}..{max(ys)}")
