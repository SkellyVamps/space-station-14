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
decals = []        # {"id", "x", "y", "color", "r"}: placed by the builder through DecalSystem
window_cells = set()   # wall cells that become a grille and reinforced window
wallmount_cells = set()  # wall cells with something mounted on them: never a window
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
               "Medical": "cyan", "Cargo": "brown", "Food": "green", "Culture": "pink", "Civilian": "grey",
               "Threats": "crimson", "Outsiders": "lime", "Awareness": "teal"}

# Portal and decal colour per wing key (matches Resources/Prototypes/Entities/Effects/tutorial_portals.yml).
WING_HEX = {"Basics": "#f0f0f0", "Command": "#3f7fe0", "Security": "#e03a3a", "Engineering": "#f59a23",
            "Science": "#a05ce0", "Medical": "#4ad6e0", "Cargo": "#b07a3c", "Food": "#5cc24a", "Culture": "#e070b8",
            "Civilian": "#9a9a9a", "Threats": "#c2185b", "Outsiders": "#b5e61d", "Awareness": "#1fa39a"}

PORTAL_COLOUR = {
    "Basics Academy": "Basics", "Command": "Command", "Security": "Security", "Engineering": "Engineering",
    "Science": "Science", "Medical": "Medical", "Cargo": "Cargo", "Service: Food": "Food",
    "Service: Culture": "Culture", "Civilian": "Civilian",
    "Antagonists: Station Threats": "Threats", "Antagonists: Outsiders": "Outsiders",
    "Antagonist Awareness": "Awareness",
}


def wing_cx(i):
    """Wings are numbered west to east separately on each side of the corridor."""
    side = WINGS[i]["side"]
    slot = sum(1 for w in WINGS[:i] if w["side"] == side)
    return FIRST_WING_X + WING_SPACING * slot


def wing_slot_count():
    return max(sum(1 for w in WINGS if w["side"] == s) for s in ("N", "S"))


BASICS_SIGNS = ["SignRedOne", "SignRedTwo", "SignRedThree", "SignRedFour", "SignRedFive", "SignRedSix"]
WING_SIGNS = {"Command": "SignBridge", "Security": "SignSecurity", "Engineering": "SignEngineering", "Science": "SignScience",
              "Medical": "SignMedical", "Cargo": "SignCargo", "Service: Food": "SignKitchen", "Service: Culture": "SignTheater",
              "Civilian": "SignReception", "Antagonists: Station Threats": "SignDanger", "Antagonists: Outsiders": "SignDanger",
              "Antagonist Awareness": "SignSecurearea"}
ROOM_SIGNS = {
    "Bridge (Captain)": "SignBridge", "Head of Personnel's Office": "SignHead", "Captain's Office": "SignHead",
    "Command Meeting Room": "SignConference", "Firing Range (Cadet)": "SignLaser", "Brig and Armory (Warden)": "SignArmory",
    "Detective's Office": "SignDetective", "Head of Security's Office": "SignHead", "Courtroom (Lawyer)": "SignLawyer",
    "Electrical Workshop": "SignElectrical", "Construction Yard (Technical Assistant)": "SignToolStorage",
    "Atmospherics Lab (Atmospheric Technician)": "SignAtmos", "Atmos Safety (alarms and fires)": "SignFire",
    "Chief Engineer's Office": "SignHead", "Engine Hall": "SignEngine", "R&D Lab (Scientist)": "SignRND",
    "Anomaly Lab": "SignAnomaly", "Artifact Lab": "SignXenobio", "Robotics": "SignRobo",
    "Research Director's Office and Servers": "SignServer", "AI Core (Station AI)": "SignAi", "Cyborg Bay": "SignRobo",
    "Chemistry Lab (Chemist)": "SignChem", "Cryo and Stasis": "SignCryogenics", "Paramedic Bay and Morgue": "SignMorgue",
    "Chief Medical Officer's Office": "SignHead", "Operating Theatre and Implants": "SignSurgery",
    "Intern Training Room": "SignExamroom", "Cargo Bay (Cargo Technician)": "SignCargoDock", "Quartermaster's Office": "SignHead",
    "Salvage Prep (Salvage Specialist)": "SignSalvage", "Mining Field": "SignNTMine", "Kitchen (Chef)": "SignKitchen",
    "Bar (Bartender)": "SignBar", "Hydroponics (Botanist)": "SignHydro1", "Janitor's Closet (Janitor)": "SignJanitor",
    "Messy Room (Janitor practice)": "SignJanitor", "Chapel (Chaplain)": "SignChapel", "Library (Librarian)": "SignLibrary",
    "Theater (Clown and Mime)": "SignTheater", "Crew Lounge (Passenger)": "SignReception",
    "Psychologist's Office": "SignPsychology", "Newsroom (Reporter)": "SignNews", "Arcade": "SignArcade",
    "Nuclear Operative": "SignRadiation", "Zombies": "SignZomlab", "Engineering Response": "SignExplosives",
    "Medical Response": "SignBiohazard",
}

# The Basics Academy route: each room's last lesson points to the next one.
BASICS_NEXT = [
    "Next: lesson room [bold]2[/bold], straight across the hallway.",
    "Next: lesson room [bold]3[/bold], further up the hallway on the left (west) side. Follow the arrows on the floor.",
    "Next: lesson room [bold]4[/bold], straight across the hallway.",
    "Next: lesson room [bold]5[/bold], further up the hallway on the left (west) side.",
    "Next: lesson room [bold]6[/bold], straight across the hallway.",
    "You have finished the Basics Academy. Pick a job and try its wing: the Portal Hall, west of the Hub, has a portal to every room.",
]


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
        ent("APCBasic", px, py, r.facing("in"), name="practice APC")
        ent("CableMV", px, py)
        ent("CableApcExtension", px, py)
    else:
        ent("APCBasic", ax, ay, r.facing("out"))
        wallmount_cells.add((ax, ay))
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
        if wing_name == "Basics Academy" and i == len(spec["papers"]) - 1:
            text += "\n\n" + BASICS_NEXT[row * 2 + (0 if west else 1)]
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
        extra = item[6] if len(item) > 6 else {}
        x, y = r.xy(u, v)
        name = "workshop target light" if spec.get("nopower") and proto == "Poweredlight" else None
        ent(proto, x, y, r.facing(f), key=key, linkTo=link, name=name, **extra)
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
            wallmount_cells.add((x, y))
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

    # door sign on the hallway side of the wall, named after the room
    sx, sy = r.xy(-1, 4)
    if wing_name == "Basics Academy":
        sign = BASICS_SIGNS[row * 2 + (0 if west else 1)]
    else:
        sign = ROOM_SIGNS.get(title, WING_SIGNS.get(wing_name, "SignNanotrasen1"))
    ent(sign, sx, sy, r.facing("in"), name=title)

    # reset button on the hallway wall, beside the door
    bx, by = r.xy(-1, 4)
    (x0, y0), (x1, y1) = r.xy(0, 0), r.xy(ROOM_W - 1, ROOM_H - 1)
    ent("TutorialRoomResetButton", bx, by, r.facing("out"),
        reset=[min(x0, x1), min(y0, y1), max(x0, x1), max(y0, y1)])
    wallmount_cells.add((bx, by))

    # windows: two panes onto the hallway, and five onto the main corridor for the first row
    for v in (1, 2):
        window_cells.add(r.xy(-1, v))
    if row == 0:
        v = -1 if side == "N" else ROOM_H
        for u in range(2, 7):
            window_cells.add(r.xy(u, v))

    # a coloured trim around the floor, in the wing's colour
    colour = WING_HEX[PORTAL_COLOUR[wing_name]] + "99"
    for u in range(ROOM_W):
        for v in range(ROOM_H):
            x, y = r.xy(u, v)
            edges = {"W": x == min(x0, x1), "E": x == max(x0, x1), "S": y == min(y0, y1), "N": y == max(y0, y1)}
            corner = "".join(k for k in ("N", "S") if edges[k]) + "".join(k for k in ("E", "W") if edges[k]).lower()
            if len(corner) == 2:
                decals.append({"id": f"BrickTileWhiteCorner{corner[0]}{corner[1]}", "x": x, "y": y, "color": colour, "r": 0})
            else:
                for k, hit in edges.items():
                    if hit:
                        decals.append({"id": f"BrickTileWhiteLine{k}", "x": x, "y": y, "color": colour, "r": 0})

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
open_space = set()  # cells kept as space: never walled (outside docking airlocks)


# ------------------------------------------------------------------ wings
def build_wing(i, wing):
    cx = wing_cx(i)
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
    # join the hallway's MV line to the main corridor's, which runs along y = 0
    mv.add((cx, sgn * 1))
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
    if wing.get("dock"):
        build_dock(wing["dock"], cx, sgn, sgn * (2 + hall_len + 1), wing["name"], i)


def build_dock(dock, cx, sgn, y, wing_name, wing_index):
    """A docking airlock at the far end of a hallway. Its GridFill docks a shuttle at round start."""
    floor(cx, y, "FloorSteel")
    ent(dock["proto"], cx, y, "S" if sgn < 0 else "N")
    for dx in (-1, 0, 1):
        open_space.add((cx + dx, y + sgn))  # the shuttle docks here
    inner = y - sgn
    ent("Table", cx + 1, inner)
    for n, text in enumerate(dock["papers"]):
        header = f"[italic]{wing_name} · {dock['title']} · lesson {n + 1} of {len(dock['papers'])}[/italic]\n\n"
        paper(cx + 1, inner, header + text, name=f"lesson {n + 1}: {dock['title']}")
    key = f"room{len(room_portals)}"
    ent(f"PortalGatewayTutorial{PORTAL_COLOUR[wing_name]}", cx - 1, inner, key=key, name="to Portal Hall")
    room_portals.append((wing_index, dock["title"], key))
    summary.append((wing_name, dock["title"], [], dock))


def build():
    # main corridor
    corridor_end = FIRST_WING_X + WING_SPACING * (wing_slot_count() - 1) + 12
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
    for y in range(0, 7):
        mv.add((0, y))
    tiles_lv_extra.add((0, 6))
    # directory papers: one per wing
    directory = "[head=2]Station directory[/head]\n\nWings, west to east along the main corridor:\n\n"
    for i, w in enumerate(WINGS):
        slot = sum(1 for v in WINGS[:i] if v["side"] == w["side"]) + 1
        directory += f"{'North' if w['side'] == 'N' else 'South'} {slot}: [bold]{w['name']}[/bold]\n"
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
    build_basics_route()
    eng = next(i for i, w in enumerate(WINGS) if w["name"] == "Engineering")
    eng_rows = math.ceil(len(WINGS[eng]["rooms"]) / 2)
    build_singularity_demo(wing_cx(eng), 2 + 8 * eng_rows)


ROUTE_COLOUR = "#ffd060cc"


def arrow(x, y, facing):
    decals.append({"id": "Arrows", "x": x, "y": y, "color": ROUTE_COLOUR, "r": FACING_ROT[facing]})


def build_basics_route():
    """Yellow floor arrows from the Hub through the Basics Academy rooms, in order."""
    cx = wing_cx(next(i for i, w in enumerate(WINGS) if w["name"] == "Basics Academy"))
    for x in range(-6, cx):
        arrow(x, 0, "E")
    for y in range(1, 2 + 8 * 3 - 4):
        if (y - 6) % 8 == 0:
            arrow(cx - 1, y, "W")   # rooms 1, 3, 5
            arrow(cx + 1, y, "E")   # rooms 2, 4, 6
        else:
            arrow(cx, y, "N")


def build_singularity_demo(cx, top_wall):
    """
    A complete singularity engine on an open platform north of the Engineering hallway, reached through
    a double airlock. Four field generators fence a singularity generator; four emitters charge them; a
    particle accelerator points at the generator. Its own RTGs power it. Nothing runs until switched on.
    """
    # double airlock out of the hallway end, with EVA suits beside it
    ent("AirlockExternalGlass", cx, top_wall)
    floor(cx, top_wall, "FloorSteel")
    floor(cx, top_wall + 1, "FloorSteel")
    ent("AirlockExternalGlass", cx, top_wall + 2)
    floor(cx, top_wall + 2, "FloorSteel")
    ent("SuitStorageEVA", cx - 1, top_wall - 1)
    ent("SuitStorageEVA", cx + 1, top_wall - 1)

    c_x, c_y = cx, top_wall + 13       # singularity generator
    x0, x1, y0, y1 = c_x - 13, c_x + 11, top_wall + 3, c_y + 10
    for x in range(x0, x1 + 1):
        for y in range(y0, y1 + 1):
            tiles[(x, y)] = "Plating"
            outdoor.add((x, y))

    ent("SingularityGenerator", c_x, c_y)
    gens = [(c_x - 3, c_y - 3), (c_x + 4, c_y - 3), (c_x - 3, c_y + 4), (c_x + 4, c_y + 4)]
    for gx, gy in gens:
        ent("ContainmentFieldGenerator", gx, gy, key=f"fieldgen{gx},{gy}")
    # emitters fire along the fence's sides into the generators
    for (gx, gy), (ex, ey, face) in zip(gens, [(c_x - 3, c_y - 7, "N"), (c_x + 4, c_y - 7, "N"),
                                                (c_x - 3, c_y + 8, "S"), (c_x + 4, c_y + 8, "S")]):
        ent("Emitter", ex, ey, face)

    # particle accelerator east of the fence, firing west at the generator.
    # Offsets are the control box's MultipartMachine layout, for a control box at rotation 0.
    cb_x, cb_y = c_x + 9, c_y + 1
    ent("ParticleAcceleratorControlBox", cb_x, cb_y)
    for proto, dx, dy in (("ParticleAcceleratorEndCap", 1, -1), ("ParticleAcceleratorFuelChamber", 0, -1),
                          ("ParticleAcceleratorPowerBox", -1, -1), ("ParticleAcceleratorEmitterPort", -2, -2),
                          ("ParticleAcceleratorEmitterFore", -2, -1), ("ParticleAcceleratorEmitterStarboard", -2, 0)):
        ent(proto, cb_x + dx, cb_y + dy, "W")

    # power: RTGs on HV, a substation to MV, MV in a ring past every emitter and to the PA power box,
    # an APC for the control box and lights.
    rx = c_x - 12
    for k in range(4):
        ent("GeneratorRTG", rx, c_y - 1 + k)
        hv.add((rx, c_y - 1 + k))
    for x in range(rx, c_x - 7 + 1):
        hv.add((x, c_y))
    ent("SubstationBasic", c_x - 7, c_y)
    ring = set()
    for x in range(c_x - 7, c_x + 12):
        ring.add((x, c_y - 7))
        ring.add((x, c_y + 8))
    for y in range(c_y - 7, c_y + 9):
        ring.add((c_x - 7, y))
        ring.add((c_x + 11, y))
    for x in range(c_x + 8, c_x + 12):
        ring.add((x, c_y))
    mv.update(ring)
    ent("APCBasic", c_x + 11, c_y + 2, "W")
    for y in range(c_y + 1, c_y + 3):
        tiles_lv_extra.add((c_x + 10, y))
        tiles_lv_extra.add((c_x + 11, y))
    tiles_lv_extra.add((cb_x, cb_y))
    tiles_lv_extra.add((cb_x, cb_y + 1))
    for lx, ly in ((c_x - 9, y0), (c_x + 9, y0), (c_x - 9, y1), (c_x + 9, y1)):
        ent("PoweredLightPostSmall", lx, ly)
        for y in range(min(ly, c_y), max(ly, c_y) + 1):
            tiles_lv_extra.add((lx, y))
    for x in range(c_x - 9, c_x + 10):
        tiles_lv_extra.add((x, c_y + 9))
        tiles_lv_extra.add((x, c_y - 8))
    for y in range(c_y - 8, c_y + 10):
        tiles_lv_extra.add((c_x + 10, y))
    # lessons on a table by the airlock
    ent("Table", cx + 2, y0)
    for n, text in enumerate(SINGULARITY_PAPERS):
        paper(cx + 2, y0, f"[italic]Engineering · Singularity Demo · lesson {n + 1} of {len(SINGULARITY_PAPERS)}[/italic]\n\n" + text,
              name=f"lesson {n + 1}: Singularity Demo")


SINGULARITY_PAPERS = [
    "[head=2]A real, fenced singularity[/head]\n\nIn front of you: a singularity generator in the middle, four containment field generators "
    "around it, an emitter aimed at each field generator, and a particle accelerator (PA) to the east. Four RTGs to the west power it all.\n\n"
    "Everything is built and wired. Nothing runs until you switch it on, and this platform is in space: keep your suit on.",
    "[head=2]Step 1: the fence[/head]\n\nClick each of the four emitters to turn it on. They fire bolts into the field generators.\n\n"
    "Click each field generator to turn it on. Once charged, generators facing each other form a glowing containment field. "
    "Wait until all four sides of the fence are up.",
    "[head=2]Step 2: the singularity[/head]\n\nOpen the PA control box (east side), turn it on and set the strength to 0. "
    "The PA fires particles into the singularity generator, which soon makes a singularity inside the fence.\n\n"
    "A singularity grows as it eats particles and anything thrown at it. Higher PA strength makes it grow faster.",
    "[head=2]Step 3: shutting down, and safety[/head]\n\nTurn the PA off. Without food the singularity slowly shrinks and disappears.\n\n"
    "Never turn off an emitter or field generator while a singularity exists: the fence drops and it escapes, eating the station. "
    "If that happens here, run restartround.",
]


def build_portal_hall(core_x0):
    """West of the hub: one row per wing, one colour-coded portal per room, each linked to that room."""
    top = 2 * len(WINGS) - 11   # first wing row; the guide row sits above it
    rect(-31, -top - 2, -13, top + 2, "FloorDark")
    floor(-12, 0, "FloorDark")
    ent("AirlockGlass", -12, 0)
    for x in range(-31, -11):
        mv.add((x, 0))
    for y in range(0, top + 4):
        mv.add((-22, y))
    ent("APCBasic", -22, top + 3, "S")
    tiles_lv_extra.add((-22, top + 3))
    for x in (-27, -19):
        ent("Poweredlight", x, top + 2, "S")
        ent("Poweredlight", x, -top - 2, "N")
    ent("Poweredlight", -31, 0, "E")

    for i, wing in enumerate(WINGS):
        y = top - 2 * i
        ent("Table", -31, y)
        paper(-31, y, f"[head=2]{wing['name']}[/head]\n\nThe {COLOUR_WORD[PORTAL_COLOUR[wing['name']]]}-coded portals in this row "
                      f"lead to the rooms of the {wing['name']} wing, in the same order as the wing's hallway. "
                      "Every room has a portal in the corner by its door that brings you back here.",
              name=f"{wing['name']} portals")
        rooms_here = [p for p in room_portals if p[0] == i]
        for k, (_, title, key) in enumerate(rooms_here):
            ent(f"PortalGatewayTutorial{PORTAL_COLOUR[wing['name']]}", -29 + 2 * k, y, portal=key, name=f"to {title}")

    # top row: the hub and the power core
    ent("PortalGatewayTutorialHub", -29, top + 2, key="core-hall", name="to Power Core")
    ent("PortalGatewayTutorialHub", core_x0 + 1, -3, portal="core-hall", name="to Portal Hall")
    ent("Table", -31, top + 2)
    paper(-31, top + 2, "[head=2]Portal Hall[/head]\n\nWalk into a portal to travel. Each portal's name (Shift + click to examine) says where it goes, "
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
                if c not in indoor and c not in outdoor and c not in open_space:
                    walls.add(c)
    for (x, y) in sorted(walls):
        tiles[(x, y)] = "Plating"
        exterior = any((x + dx, y + dy) not in indoor and (x + dx, y + dy) not in walls
                       for dx in (-1, 0, 1) for dy in (-1, 0, 1))
        if (x, y) in window_cells and (x, y) not in wallmount_cells:
            ent("Grille", x, y)
            ent("ReinforcedWindow", x, y)
            continue
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
        "decals": decals,
    }
    with open(sys.argv[1], "w") as f:
        json.dump(out, f)
    if len(sys.argv) > 2:
        write_summary(sys.argv[2])
    xs = [x for x, _ in tiles]
    ys = [y for _, y in tiles]
    print(f"{len(tiles)} tiles, {len(ents)} entities, {len(summary)} rooms, "
          f"bounds x {min(xs)}..{max(xs)} y {min(ys)}..{max(ys)}")
