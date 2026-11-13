"""
Room content for the Tutorial Station.

Every room is a dict:
  title     shown at the top of each lesson paper
  jobs      job ids taught here (their spawn points are placed in the room)
  floor     tile id for the room floor
  wall      machines/lockers placed along the walls, facing into the room
  table     items placed on the room's centre tables
  floor_items  things placed on free floor (mobs, canisters, crates)
  place     explicit placements: (proto, u, v, facing) where u is the distance from
            the hallway wall (0..8) and v runs along it (0..6); facing is
            "in" (toward the hallway), "out", "up" (+v), "down" (-v) or None
  papers    lesson texts, numbered in order
  nopower   True keeps the room off the station grid (Electrical Workshop)
"""

KEYS = (
    "Default keys: [bold]WASD[/bold] move, [bold]E[/bold] use what you click, "
    "[bold]Z[/bold] use the item in your hand, [bold]X[/bold] swap hands, [bold]Q[/bold] drop."
)


def paper(title, *lines):
    return f"[head=2]{title}[/head]\n\n" + "\n\n".join(lines)


SPACE_LAW_HINT = "Space Law is in the green book on the Courtroom table (Security wing)."

# ---------------------------------------------------------------- Hub
HUB_PAPERS = [
    paper(
        "Welcome to the Tutorial Station",
        "This station teaches every crew job in Space Station 14. Nothing here will hurt you unless you go looking for it.",
        "[bold]North wings:[/bold] Basics Academy, Command, Security, Engineering, Science.\n"
        "[bold]South wings:[/bold] Medical, Cargo, Service: Food, Service: Culture, Civilian.\n"
        "The Station Power Core is at the far east end of the main corridor.",
        "[bold]Portal Hall[/bold]: through the door on the west side of this hub. It has a colour-coded portal to every room on the station, "
        "and every room has a portal back.",
        "Start with the [bold]Basics Academy[/bold], the first wing north of this hub. Its rooms are numbered 1 to 6: follow the yellow arrows on the floor.",
        "Every room has a lesson table just inside the door. Read the papers in number order.",
    ),
    paper(
        "Switching jobs",
        "Every job has unlimited slots on this map.",
        "To try another job: open the console with [bold]`[/bold] (the key under Esc), "
        "type [bold]respawn[/bold] and press Enter. In the lobby, press [bold]Join Game[/bold] and pick a job.",
        "Late joiners arrive here in the hub, so walk to that job's wing. If you set the job as "
        "your top priority before the round starts, you begin inside its room instead.",
        "To reset everything, type [bold]restartround[/bold].",
    ),
    paper(
        "Getting help in game",
        "[bold]Shift + left click[/bold] examines anything and tells you what it is.",
        "[bold]Numpad 0[/bold] opens the Guidebook, the game's built-in manual. Every lesson here points to the matching Guidebook page.",
        KEYS,
    ),
]

# ---------------------------------------------------------------- Basics Academy
BASICS = [
    dict(
        title="1. Moving and Interacting",
        floor="FloorSteel",
        floor_items=["CrateGenericSteel", "Chair", "ChairOfficeDark"],
        table=["ToolboxMechanicalFilled", "FlashlightLantern", "Paper"],
        papers=[
            paper(
                "Moving around",
                "[bold]W A S D[/bold] walk. Hold [bold]Shift[/bold] to walk slowly and quietly.",
                "Doors open when you walk into them. This room's door has no access lock, but most real doors only open for the right ID card.",
                "Try it: walk into the hallway and back.",
            ),
            paper(
                "Clicking things",
                "[bold]Left click[/bold] an object to use it or pick it up. [bold]E[/bold] does the same to whatever is under the cursor.",
                "[bold]Right click[/bold] opens the context menu: a list of everything on that tile, and each one's verbs (pick up, examine, rotate...).",
                "[bold]Shift + left click[/bold] examines. Examine everything in this room.",
            ),
            paper(
                "Pulling and throwing",
                "[bold]Ctrl + left click[/bold] grabs and pulls an object or person. Walk away and it follows. [bold]H[/bold] lets go.",
                "Try it: pull the steel crate across the room.",
                "[bold]Ctrl + Q[/bold] throws what is in your hand toward the cursor. Throw the flashlight at the wall, then pick it up again.",
            ),
        ],
    ),
    dict(
        title="2. Hands, Inventory and Storage",
        floor="FloorSteel",
        table=["ClothingBackpack", "ClothingBeltUtilityFilled", "ToolboxEmergencyFilled", "BoxCardboard", "ClothingHeadHatHardhatYellow"],
        papers=[
            paper(
                "Your two hands",
                "You have a left and a right hand. Only the [bold]active[/bold] hand (highlighted at the bottom of the screen) does things.",
                "[bold]X[/bold] swaps the active hand. [bold]Q[/bold] drops what is in it. [bold]Z[/bold] uses the held item (turns a flashlight on, opens a box).",
                "Try it: pick up the cardboard box, press Z to open it, close it, then drop it.",
            ),
            paper(
                "Equipment slots",
                "[bold]I[/bold] shows your inventory: head, eyes, ears, mask, neck, jumpsuit, outer suit, gloves, shoes, ID, belt, back, pockets and suit storage.",
                "Hold an item and click a slot to wear it. Drag it out to take it off.",
                "Shortcuts: [bold]Shift+B[/bold] puts the held item in (or takes it from) your backpack. [bold]Shift+E[/bold] does the same for your belt, "
                "[bold]Shift+F[/bold] and [bold]Shift+G[/bold] for your pockets.",
                "Try it: wear the backpack and the hard hat, put the toolbox in the backpack, then take it back out with Shift+B.",
            ),
            paper(
                "Storage",
                "[bold]V[/bold] opens your backpack. Containers show their items in a grid: bigger items take more cells.",
                "Click an item in the grid to take it. Click the grid while holding an item to put it in.",
                "The utility belt only holds tools. Try putting the box on the belt and read the message you get.",
            ),
        ],
    ),
    dict(
        title="3. Survival and EVA",
        floor="FloorSteel",
        eva=True,
        wall=["VendingMachineTankDispenserEVA", "SuitStorageEVA"],
        table=["ClothingOuterHardsuitEVA", "ClothingHeadHelmetEVA", "OxygenTankFilled", "ClothingMaskBreath", "JetpackBlueFilled", "ClothingOuterSuitEmergency"],
        papers=[
            paper(
                "Breathing",
                "You need air. If the air runs out you lose your breath, take suffocation damage and soon pass out.",
                "Your survival box (in your backpack) has a breath mask and an emergency oxygen tank.",
                "To use internals: wear a breath mask, put a tank in your suit storage, belt or pocket, then click the tank's "
                "lungs action button (top left of the screen). The same button turns them off.",
            ),
            paper(
                "Spacewalking",
                "Space has no air, no pressure and is extremely cold. To go outside you need a pressure suit, a helmet and internals.",
                "Suit up with the EVA suit and helmet on this table, turn on internals, then go through the two external airlocks in the far wall. "
                "Only open one airlock at a time, or this room's air leaks out.",
                "Outside there is a catwalk. With no gravity you drift: grab the catwalk railings and push off, "
                "or use the jetpack (activate it from its action button).",
            ),
            paper(
                "When things go wrong",
                "A [bold]hull breach[/bold] is a hole to space: air rushes out and pulls you toward it. Get out, close the doors behind you, and tell Engineering.",
                "[bold]Firelocks[/bold] (yellow doors) drop shut when the pressure or temperature is wrong. Do not force them open without a suit.",
                "Fires: use a fire extinguisher, or if you are burning, roll on the floor (lie down and get up).",
            ),
        ],
    ),
    dict(
        title="4. Health and Damage",
        floor="FloorWhite",
        floor_items=["Bed"],
        table=["MedkitFilled", "MedkitBruteFilled", "MedkitBurnFilled", "HandheldHealthAnalyzer", "MedkitOxygenFilled"],
        papers=[
            paper(
                "Damage types",
                "[bold]Brute[/bold]: blunt, slash, piercing. [bold]Burn[/bold]: heat, shock, cold, caustic. "
                "[bold]Toxin[/bold], [bold]Airloss[/bold] (suffocation), [bold]Genetic[/bold] and [bold]Radiation[/bold].",
                "At 100 total damage you go [bold]critical[/bold]: you fall over, cannot act and slowly get worse. At 200 you are [bold]dead[/bold].",
                "Click the health icon at the top right to see your own state.",
            ),
            paper(
                "First aid",
                "Brute medkit: bruise packs and gauze. Burn medkit: ointment. Use them on yourself or others by holding them and clicking the patient.",
                "The health analyzer shows exactly what damage someone has. Click a person with it.",
                "Try it: scan yourself with the analyzer.",
            ),
            paper(
                "Dying is not the end",
                "Dead players become ghosts. Ghosts can watch the round and, on a real server, may be revived by Medical.",
                "On this map you can always type [bold]respawn[/bold] in the console to come back as a new character.",
            ),
        ],
    ),
    dict(
        title="5. Crafting and Construction",
        floor="FloorSteel",
        floor_items=["CrateGenericSteel"],
        table=["SheetSteel", "SheetGlass", "PartRodMetal", "ToolboxMechanicalFilled", "CableApcStack"],
        papers=[
            paper(
                "The crafting menu",
                "Press [bold]G[/bold] to open the construction and crafting menu. Search for what you want to build.",
                "Each recipe lists what you need. Some are items made in your hands, others are structures you place on the floor as a blue ghost.",
                "Try it: craft a steel chair. Open G, search \"chair\", pick it, then click a free floor tile. Keep the steel sheets in your hand or nearby.",
            ),
            paper(
                "Taking things apart",
                "Most structures come apart with tools. Examine a structure (Shift + click) and it often tells you which tool to use next.",
                "Common tools: [bold]wrench[/bold] (anchor and unanchor), [bold]screwdriver[/bold], [bold]crowbar[/bold] (pry tiles and doors), "
                "[bold]wirecutters[/bold], [bold]welder[/bold] and [bold]multitool[/bold].",
                "Try it: unanchor a chair with the wrench, move it, then anchor it again.",
            ),
        ],
    ),
    dict(
        title="6. Talking, Radio and Your ID",
        floor="FloorSteel",
        wall=["IntercomCommon"],
        table=["ClothingHeadsetGrey", "Pen", "PaperBin10", "PersonalAI"],
        floor_items=["FaxMachineBase"],
        papers=[
            paper(
                "Chat",
                "Press [bold]T[/bold] to type in chat, then Enter. People near you can hear it.",
                "[bold]Comma[/bold] (,) whispers. [bold]Shift + '[/bold] does an emote (*waves*). [bold]Tab[/bold] cycles the chat channel.",
            ),
            paper(
                "Radio",
                "A headset on your ears lets you talk to the whole station. Start the message with a prefix:",
                "[bold];[/bold] Common   [bold]:c[/bold] Command   [bold]:e[/bold] Engineering   [bold]:m[/bold] Medical   "
                "[bold]:n[/bold] Science   [bold]:s[/bold] Security   [bold]:u[/bold] Supply (Cargo)   [bold]:v[/bold] Service",
                "Your headset only has the channels your job's encryption keys give. Examine it to see them. Try it: say \";hello\" with the headset on.",
            ),
            paper(
                "ID card and PDA",
                "Your ID card decides which doors and lockers open for you. It lives in your PDA, in the ID slot.",
                "Use the PDA (Z while holding it) to see the crew manifest, notes, the flashlight and job programs.",
                "The fax machine sends paper to other fax machines. Write on paper by holding a pen and clicking the paper.",
            ),
        ],
    ),
]

# ---------------------------------------------------------------- Command
COMMAND = [
    dict(
        title="Bridge (Captain)",
        jobs=["Captain"],
        floor="FloorDark",
        door="AirlockCommandGlass",
        wall=["ComputerComms", "ComputerCrewMonitoring", "ComputerStationRecords", "ComputerAlert", "ComputerSurveillanceCameraMonitor"],
        floor_items=["ChairOfficeDark", "ChairOfficeDark"],
        papers=[
            paper(
                "Captain: your job",
                "You are in charge of the station and responsible for every department. Delegate to the heads of staff and step in when they cannot handle it.",
                "You carry the nuclear authentication disk. Keep it on you: losing it is how a station gets blown up.",
                "You have access to almost everything. Use it sparingly; heads of staff run their own departments.",
            ),
            paper(
                "The bridge consoles",
                "[bold]Communications console[/bold]: station-wide announcements, alert levels (green, blue, red...) and calling the evacuation shuttle.",
                "[bold]Crew monitoring[/bold]: the location and health of crew with suit sensors on.",
                "[bold]Station records[/bold] and [bold]camera monitor[/bold]: who is aboard and what the cameras see.",
                "Try it: use the communications console to make an announcement.",
            ),
        ],
    ),
    dict(
        title="Head of Personnel's Office",
        jobs=["HeadOfPersonnel"],
        floor="FloorDark",
        door="AirlockCommandGlass",
        wall=["ComputerId", "LockerHeadOfPersonnelFilled", "VendingMachineCart"],
        table=["PaperBin10", "Pen", "RubberStampApproved", "RubberStampDenied", "BoxPDA"],
        papers=[
            paper(
                "Head of Personnel: your job",
                "You run the Service department and handle the crew's paperwork: job changes, access changes and new ID cards.",
                "You are second in command. If the Captain is missing, you are usually the acting Captain.",
            ),
            paper(
                "Changing access",
                "The ID card console needs two cards: yours (with ID console access) in the top slot, and the card to edit in the bottom slot.",
                "Pick a job preset or tick individual accesses, then eject the card.",
                "Try it: take a spare PDA from the box, put its ID card in the target slot, give it Bar access and eject it.",
            ),
        ],
    ),
    dict(
        title="Captain's Office",
        floor="FloorWood",
        door="AirlockCommandGlass",
        wall=["LockerCaptainFilled", "SuitStorageCaptain", "BookshelfFilled"],
        floor_items=["ComfyChair"],
        papers=[
            paper(
                "Captain's gear",
                "Your locker (Captain access only) holds your spare uniforms and the captain's equipment.",
                "The suit storage unit holds the captain's pressure suit for emergencies.",
            ),
            paper(
                "Good captaincy",
                "Read the station's mood: answer requests on the Command radio channel (:c) and keep heads of staff informed.",
                "Raise the alert level when there is a real threat. Call the evacuation shuttle when the station can no longer be saved.",
            ),
        ],
    ),
    dict(
        title="Command Meeting Room",
        floor="FloorDark",
        door="AirlockCommandGlass",
        floor_items=["ChairOfficeDark", "ChairOfficeDark", "ChairOfficeDark", "ChairOfficeDark"],
        table=["PaperBin10", "Pen"],
        papers=[
            paper(
                "Heads of staff",
                "Every department has a head: Head of Personnel (Service), Head of Security, Chief Engineer, Chief Medical Officer, "
                "Research Director and Quartermaster (Cargo).",
                "Heads hand out access in their department, keep their crew safe and report to the Captain. Each head's room in this tutorial is inside its own wing.",
            ),
            paper(
                "Alert levels",
                "Green: all clear. Blue: a suspected threat; Security may search people. Red: a confirmed threat to the station. "
                "Gamma and Delta: the station is in extreme danger.",
                "Only the Captain, or an acting Captain, should change the alert level.",
            ),
        ],
    ),
]

# ---------------------------------------------------------------- Security
SECURITY = [
    dict(
        title="Security Equipment Room (Officer)",
        jobs=["SecurityOfficer"],
        floor="FloorDark",
        door="AirlockSecurityGlass",
        wall=["LockerSecurityFilled", "LockerSecurityFilled", "VendingMachineSec", "VendingMachineSecDrobe", "ComputerCriminalRecords", "WeaponCapacitorRecharger"],
        table=["Handcuffs", "Flash"],
        papers=[
            paper(
                "Security Officer: your job",
                "You protect the crew and enforce Space Law. Patrol, answer calls on the Security channel (:s) and arrest criminals.",
                SPACE_LAW_HINT,
            ),
            paper(
                "Your tools",
                "[bold]Disabler[/bold]: fires stamina-draining bolts. Enough hits and the target falls down. Recharge it in the recharger.",
                "[bold]Stun baton[/bold]: turn it on (Z) and hit someone to drain their stamina quickly.",
                "[bold]Flash[/bold]: blinds people in front of you for a moment. [bold]Handcuffs[/bold]: hold them and click a downed or willing person.",
                "Practise on the targets in the Firing Range next door.",
            ),
            paper(
                "Making an arrest",
                "1. Tell the suspect what they are being arrested for. 2. Stun or disable them if they resist. 3. Cuff them. "
                "4. Pull them (Ctrl + click) to the brig. 5. Set the criminal record status to Detained. 6. Hand them to the Warden.",
                "Combat mode (the crossed swords button by your hands) makes clicks attack instead of interact.",
            ),
        ],
    ),
    dict(
        title="Firing Range (Cadet)",
        jobs=["SecurityCadet"],
        floor="FloorDark",
        door="AirlockSecurityGlass",
        place=[("TargetHuman", 8, 1, None), ("TargetSyndicate", 8, 3, None), ("TargetClown", 8, 5, None)],
        table=["WeaponDisablerPractice", "WeaponLaserCarbinePractice", "Stunbaton"],
        papers=[
            paper(
                "Security Cadet: your job",
                "Cadets are officers in training. Follow a full officer around, learn Space Law and the station's layout.",
                "You start with less gear than an officer. Ask the Head of Security or the Warden before taking weapons from the armory.",
            ),
            paper(
                "Shooting",
                "Pick up a practice gun. Turn on combat mode (crossed swords button), aim at a target with the mouse and click to fire.",
                "Practice weapons do no harm, so shoot all you like. Energy weapons run out of charge; recharge them in the Equipment Room.",
                "Try it: hit each target five times.",
            ),
        ],
    ),
    dict(
        title="Brig and Armory (Warden)",
        jobs=["Warden"],
        floor="FloorDark",
        door="AirlockSecurityGlass",
        cell=True,
        wall=["LockerWardenFilled", "ComputerCriminalRecords", "GunSafeDisabler"],
        place=[("WardrobePrisonFilled", 8, 6, "in"), ("Bed", 8, 0, None)],
        papers=[
            paper(
                "Warden: your job",
                "You run the brig and the armory. Process prisoners, keep their sentences, and hand out armory weapons when the Head of Security allows it.",
                "The cell at the back of this room is a practice cell. Walk in and out to see it.",
            ),
            paper(
                "Processing a prisoner",
                "1. Search the prisoner and take anything dangerous or illegal. 2. Note the crime and sentence in the criminal records console. "
                "3. Give them a prison jumpsuit from the wardrobe. 4. Put them in a cell and set the brig timer. 5. Release them when the timer ends.",
                "Sentences come from Space Law. " + SPACE_LAW_HINT,
            ),
        ],
    ),
    dict(
        title="Detective's Office",
        jobs=["Detective"],
        floor="FloorWood",
        door="AirlockSecurityGlass",
        wall=["LockerDetectiveFilled", "VendingMachineDetDrobe", "ComputerCriminalRecords"],
        floor_items=["PuddleBlood"],
        table=["BoxEvidenceMarkers", "BoxForensicPad", "KitchenKnife"],
        papers=[
            paper(
                "Detective: your job",
                "You investigate crimes. Find out who did it with forensics, interviews and records, then pass your evidence to Security.",
                "You are not a frontline officer. Leave arrests to them when you can.",
            ),
            paper(
                "Forensics",
                "Your forensic scanner (from your starting gear or locker) reads fingerprints, glove fibres and DNA left on objects, then prints a report.",
                "Forensic pads take fingerprints straight from someone's hands. Compare them with the scanner's results.",
                "Try it: pick up the knife on the table, then scan it. Your own fingerprints are on it now. Take your prints with a pad and compare.",
                "Place evidence markers next to clues so others can find them.",
            ),
        ],
    ),
    dict(
        title="Head of Security's Office",
        jobs=["HeadOfSecurity"],
        floor="FloorDark",
        door="AirlockSecurityGlass",
        wall=["LockerHeadOfSecurityFilled", "ComputerSurveillanceCameraMonitor", "ComputerCriminalRecords"],
        floor_items=["ChairOfficeDark"],
        papers=[
            paper(
                "Head of Security: your job",
                "You lead Security: officers, cadets, the Warden and the Detective. Assign patrols, approve armory use and handle the big threats.",
                "You answer to the Captain. Security's job is to keep the crew alive, not to win fights.",
            ),
            paper(
                "Running a shift",
                "Check the criminal records console for wanted people. Use the camera console to watch trouble spots.",
                "Keep your team informed on the Security channel (:s). Ask Command (:c) before raising the alert level.",
            ),
        ],
    ),
    dict(
        title="Courtroom (Lawyer)",
        jobs=["Lawyer"],
        floor="FloorWood",
        door="AirlockGlass",
        wall=["VendingMachineLawDrobe", "BookshelfFilled"],
        floor_items=["ChairWood", "ChairWood", "ChairWood"],
        table=["BookSpaceLaw", "BriefcaseBrownFilled", "PaperBin10", "Pen"],
        papers=[
            paper(
                "Lawyer: your job",
                "You defend crew members accused of crimes and make sure Security follows Space Law.",
                "Read the Space Law book on the table. Know the crime codes and the sentence for each.",
            ),
            paper(
                "Defending a client",
                "Visit the brig and ask the Warden to see your client. Hear their side and check their record.",
                "If the arrest or sentence breaks Space Law, raise it with the Warden or the Head of Security. Bring it to the Captain if they will not listen.",
            ),
        ],
    ),
]

# ---------------------------------------------------------------- Engineering
ENGINEERING = [
    dict(
        title="Engineering Locker Room (Station Engineer)",
        jobs=["StationEngineer"],
        floor="FloorSteel",
        door="AirlockEngineeringGlass",
        wall=["LockerEngineerFilled", "LockerEngineerFilled", "VendingMachineEngivend", "VendingMachineYouTool", "VendingMachineEngiDrobe", "ComputerPowerMonitoring"],
        table=["ToolboxElectricalFilled", "ToolboxMechanicalFilled", "Multitool"],
        papers=[
            paper(
                "Station Engineer: your job",
                "You keep the station powered and in one piece: run the engine, repair breaches, fix broken machines and wiring.",
                "Your tools are in your belt and locker. A multitool reads wires and links machines; insulated gloves stop shocks.",
            ),
            paper(
                "Where to learn each skill",
                "[bold]Electrical Workshop[/bold] (next door): build a working power circuit.",
                "[bold]Construction Yard[/bold]: walls, floors, windows and repairs.",
                "[bold]Engine Hall[/bold]: the kinds of engine and how to start them safely.",
                "[bold]Atmospherics Lab[/bold] and [bold]Atmos Safety[/bold]: gases, pumps, fires and alarms.",
            ),
        ],
    ),
    dict(
        title="Electrical Workshop",
        floor="FloorSteel",
        door="AirlockEngineeringGlass",
        nopower=True,
        place=[
            ("PortableGeneratorPacman", 7, 1, None),
            ("SubstationBasicEmpty", 7, 5, None),
            ("Poweredlight", 8, 3, "in"),
        ],
        floor_items=[],
        table=["SheetPlasma", "CableHVStack", "CableMVStack", "CableApcStack", "ToolboxElectricalFilled", "ClothingHandsGlovesColorYellow"],
        apc_lesson=True,
        papers=[
            paper(
                "Power in three steps",
                "Power flows over three kinds of cable: [bold]HV[/bold] (high voltage, from generators), [bold]MV[/bold] (medium voltage, from substations) "
                "and [bold]LV[/bold] (the thin apc cables, from APCs to machines).",
                "Generator → HV → substation → MV → APC → LV → lights and machines.",
                "This room is not on the station's power grid. Its lights are emergency lights. Make the big light on the far wall turn on.",
            ),
            paper(
                "Step 1: the generator",
                "The P.A.C.M.A.N. generator burns plasma. Wrench it to anchor it, put plasma sheets in it, then turn it on in its menu.",
                "Lay HV cable from the generator's tile to the substation's tile: hold the HV cable coil and click each floor tile. "
                "Cables go under the floor, so you may need to pry up tiles with a crowbar first.",
            ),
            paper(
                "Step 2: substation and APC",
                "The substation turns HV into MV. Lay MV cable from the substation to the practice APC on the far wall. A stub of MV and LV cable is already under the APC.",
                "The APC powers the room. Lay LV (apc) cable from the practice APC on the far wall to the light. Click the APC to see its charge and turn breakers on.",
                "Wear insulated gloves. Live cables shock you when you cut them.",
            ),
            paper(
                "Checking your work",
                "Examine the substation and APC: they tell you if they are getting power. A multitool on a cable shows the network's supply and load.",
                "When the light turns on, you are done. The station's real power core, at the east end of the main corridor, works the same way.",
            ),
        ],
    ),
    dict(
        title="Construction Yard (Technical Assistant)",
        jobs=["TechnicalAssistant"],
        floor="FloorSteel",
        door="AirlockEngineeringGlass",
        tiles={(6, 1): "Plating", (6, 2): "Plating", (7, 1): "Plating", (7, 2): "Plating"},
        place=[("Grille", 7, 5, None), ("GrilleBroken", 6, 5, None)],
        table=["SheetSteel", "SheetGlass", "SheetRGlass", "PartRodMetal", "FloorTileItemSteel", "ToolboxMechanicalFilled", "Welder"],
        papers=[
            paper(
                "Technical Assistant: your job",
                "You are an engineer in training. Help the Station Engineers with repairs and learn the trade. Do not touch the engine without supervision.",
            ),
            paper(
                "Floors",
                "The four grey squares near the far wall are bare plating. Hold floor tiles and click each one to tile it.",
                "A crowbar pries tiles back up. Plating with no tile shows the cables and pipes underneath.",
            ),
            paper(
                "Walls and windows",
                "Walls: build a girder from metal rods or steel (G menu, \"girder\"), then add steel sheets to the girder.",
                "Windows: put glass on a grille. One grille on the far wall is whole: click it with glass sheets. The other is broken: fix it with rods first.",
                "Deconstruct walls with a welder (weld the wall) then a wrench. Reinforced walls take more steps; examine them as you go.",
            ),
        ],
    ),
    dict(
        title="Atmospherics Lab (Atmospheric Technician)",
        jobs=["AtmosphericTechnician"],
        floor="FloorSteel",
        door="AirlockAtmosphericsGlass",
        wall=["LockerAtmosphericsFilled", "VendingMachineAtmosDrobe"],
        floor_items=["OxygenCanister", "NitrogenCanister", "AirCanister", "StorageCanister", "CarbonDioxideCanister", "PortableScrubber"],
        table=["GasAnalyzer", "ClothingHandsGlovesColorYellow", "Wrench"],
        papers=[
            paper(
                "Atmospheric Technician: your job",
                "You keep the station's air breathable: right pressure, right mix, right temperature. You also fight fires and fix leaks.",
                "Normal station air is about 21% oxygen and 79% nitrogen at about 101 kPa and 20 °C.",
            ),
            paper(
                "Gases",
                "[bold]Oxygen[/bold]: breathable, feeds fires. [bold]Nitrogen[/bold]: harmless filler. [bold]Carbon dioxide[/bold]: poisonous in quantity. "
                "[bold]Plasma[/bold]: toxic and burns hot. [bold]Nitrous oxide[/bold]: puts people to sleep. [bold]Tritium[/bold] and [bold]water vapour[/bold] are rarer.",
                "Use the gas analyzer on a tile, a canister or a pipe to read pressure, temperature and the gas mix.",
                "Try it: analyze the air canister and this room's air.",
            ),
            paper(
                "Canisters and pipes",
                "Canisters store gas. Wrench one onto a connector port to feed it into a pipe network, or open its valve to release gas into the room.",
                "Pumps move gas through pipes, filters pull one gas out of a mix and mixers combine two inputs. Set each one in its menu.",
                "The portable scrubber cleans bad gases out of a room. Turn it on and leave it.",
            ),
        ],
    ),
    dict(
        title="Atmos Safety (alarms and fires)",
        floor="FloorSteel",
        door="AirlockAtmosphericsGlass",
        wall=["AirAlarm", "FireAlarm", "ExtinguisherCabinetFilled"],
        place=[("Firelock", 4, 0, None), ("Firelock", 4, 1, None), ("Firelock", 4, 2, None), ("Firelock", 4, 3, None),
               ("Firelock", 4, 4, None), ("Firelock", 4, 5, None), ("Firelock", 4, 6, None)],
        table=["HolofanProjector", "ClothingOuterSuitFire", "ClothingHeadHelmetFire", "FireExtinguisher"],
        notable=True,
        papers=[
            paper(
                "Alarms",
                "The [bold]air alarm[/bold] watches a room's air. Click it to see the readings and set the vents and scrubbers it controls.",
                "The [bold]fire alarm[/bold] closes every firelock in its area. Click it to trigger or reset it.",
                "The line of yellow doors across this room is a row of firelocks. Pull the fire alarm and watch them close, then reset it.",
            ),
            paper(
                "Fighting fires",
                "Wear the fire suit and helmet. Aim the extinguisher at the flames and click. A fire needs oxygen, so sealing a room starves it.",
                "The holofan projector makes a barrier that blocks air but lets people through. Use it to seal a breach while you fix it.",
            ),
        ],
    ),
    dict(
        title="Chief Engineer's Office",
        jobs=["ChiefEngineer"],
        floor="FloorDark",
        door="AirlockEngineeringGlass",
        wall=["LockerChiefEngineerFilled", "ComputerPowerMonitoring", "ComputerAtmosMonitoring", "ComputerSolarControl"],
        floor_items=["ChairOfficeDark"],
        papers=[
            paper(
                "Chief Engineer: your job",
                "You lead Engineering and Atmospherics. Choose and start the engine, assign repairs and keep power and air working all round.",
                "Your locker has the advanced tools. The power monitoring console shows every generator, battery and big load on the station.",
            ),
            paper(
                "First ten minutes of a real shift",
                "1. Get the engine running (see the Engine Hall). 2. Check that the SMES batteries are charging. 3. Set up solars as a backup. "
                "4. Send an atmos tech to check the distribution loop. 5. Assign engineers to areas of the station.",
            ),
        ],
    ),
    dict(
        title="Engine Hall",
        floor="FloorSteel",
        door="AirlockEngineeringGlass",
        floor_items=["ContainmentFieldGenerator", "ContainmentFieldGenerator", "EmitterFlatpack", "SolarPanel", "CrateEngineeringAMEShielding", "CrateEngineeringAMEJar"],
        place=[
            ("ParticleAcceleratorControlBoxUnfinished", 8, 1, None),
            ("ParticleAcceleratorEndCapUnfinished", 8, 2, None),
            ("ParticleAcceleratorFuelChamberUnfinished", 8, 3, None),
            ("ParticleAcceleratorPowerBoxUnfinished", 8, 4, None),
        ],
        papers=[
            paper(
                "Engines",
                "Stations run on one main engine. The common ones:",
                "[bold]Singularity[/bold]: a particle accelerator feeds a gravitational singularity held by a containment field. Lots of power; disaster if it escapes.",
                "[bold]Tesla[/bold]: like the singularity, but a ball of lightning hits tesla coils. Grounding rods protect the station.",
                "[bold]AME[/bold] (antimatter engine): shielding plus fuel jars. Simple and safe if you do not overload it.",
                "[bold]Solars[/bold] and [bold]TEG[/bold] (thermo-electric generator) are backups or advanced setups.",
            ),
            paper(
                "What is in this room",
                "Unfinished particle accelerator parts, two containment field generators, a boxed emitter, AME parts and a solar panel. "
                "There is no singularity generator here, so nothing can go wrong.",
                "Try it: finish the particle accelerator parts with a wrench, cables and a screwdriver. Examine each part to see its next step.",
                "The station's power on this map comes from the Power Core at the east end of the main corridor. Its gravity generator is there too.",
            ),
            paper(
                "The singularity demo",
                "A complete, fenced singularity engine waits outside, through the double airlock at the top of the Engineering hallway. "
                "Suit up first: it is in space. EVA suits are beside the airlock.",
                "The papers on the platform walk you through starting it. Everything is built and powered; nothing runs until you switch it on.",
            ),
        ],
    ),
]

# ---------------------------------------------------------------- Science
SCIENCE = [
    dict(
        title="R&D Lab (Scientist)",
        jobs=["Scientist"],
        floor="FloorWhite",
        door="AirlockScienceGlass",
        wall=["ComputerResearchAndDevelopment", "Protolathe", "CircuitImprinter", "Autolathe", "LockerScienceFilled", "VendingMachineSciDrobe"],
        table=["SheetSteel", "SheetGlass", "SheetPlastic"],
        papers=[
            paper(
                "Scientist: your job",
                "You research new technology for the station: anomalies, artifacts and robotics earn research points, which unlock designs.",
                "Unlocked designs are printed on lathes, which other departments rely on.",
            ),
            paper(
                "Researching",
                "The R&D console shows the technology tree. Spend points to unlock a technology; its designs appear in the lathes.",
                "Points come from the research server (in the Research Director's office here), which collects them from anomaly vessels and artifact analysis.",
                "Try it: open the R&D console and read the tree.",
            ),
            paper(
                "Lathes",
                "Load materials by clicking a lathe with sheets. Then pick a design and press Print.",
                "Autolathe: basic tools and parts. Protolathe: advanced researched items. Circuit imprinter: machine and computer boards.",
                "Try it: load steel into the autolathe and print a crowbar.",
            ),
        ],
    ),
    dict(
        title="Research Assistant's Desk",
        jobs=["ResearchAssistant"],
        floor="FloorWhite",
        door="AirlockScienceGlass",
        table=["AnomalyScanner", "NodeScanner", "BookScientistsGuidebook"],
        floor_items=["ChairOfficeLight"],
        papers=[
            paper(
                "Research Assistant: your job",
                "You are a scientist in training. Help with anomalies and artifacts, and ask a scientist before you touch anything dangerous.",
                "Good first tasks: scan anomalies, carry artifacts to the analyzer and load the lathes.",
            ),
            paper(
                "Scanners",
                "The anomaly scanner reads an anomaly's type, stability and the particle that affects it.",
                "The node scanner reads an artifact's active node and what triggers it.",
                "Both labs are in this wing. Take these scanners with you.",
            ),
        ],
    ),
    dict(
        title="Anomaly Lab",
        floor="FloorWhite",
        door="AirlockScienceGlass",
        wall=["MachineAnomalyGenerator", "MachineAnomalyVessel", "MachineAPE"],
        table=["AnomalyScanner", "SheetPlasma"],
        papers=[
            paper(
                "Anomalies",
                "Anomalies are unstable objects that appear around the station. They pulse every so often, and each type does something different (fire, electricity, gravity...).",
                "Left alone they grow and eventually go supercritical, which is very bad. Scientists keep them stable and harvest points from them.",
            ),
            paper(
                "Working an anomaly",
                "1. Scan it with the anomaly scanner. 2. Link an anomaly vessel to it with the scanner to start earning points. "
                "3. Use the A.P.E. to fire particles at it: the right particle changes its stability, severity or health.",
                "The anomaly generator makes a new anomaly using plasma. On a real station it appears somewhere random, so warn the crew first.",
            ),
        ],
    ),
    dict(
        title="Artifact Lab",
        floor="FloorWhite",
        door="AirlockScienceGlass",
        wall=["CrateArtifactContainer"],
        place=[("MachineArtifactAnalyzer", 6, 3, None, "analyzer"), ("ComputerAnalysisConsole", 8, 3, "in", None, "analyzer")],
        floor_items=["RandomArtifactSpawner"],
        table=["NodeScanner"],
        papers=[
            paper(
                "Artifacts",
                "Alien artifacts do something when triggered: heat, cold, gas, teleporting, healing... Each has a tree of nodes, and finding new nodes earns points.",
                "Triggers include touching it, hitting it, heating or cooling it, and many more. The node scanner tells you the active node.",
            ),
            paper(
                "Analyzing",
                "Push the artifact onto the analyzer pad. The analysis console next to it is already linked to the pad.",
                "Use the console to scan the artifact, then extract points. Trigger it to reach a new node and repeat.",
                "Keep dangerous artifacts in the artifact crate.",
            ),
        ],
    ),
    dict(
        title="Robotics",
        floor="FloorWhite",
        door="AirlockScienceGlass",
        wall=["ExosuitFabricator", "VendingMachineRobotics", "VendingMachineRoboDrobe", "ComputerRoboticsControl"],
        table=["CyborgEndoskeleton", "MMI", "PowerCellHigh", "SheetSteel"],
        papers=[
            paper(
                "Robotics",
                "Roboticists build cyborgs and mechs in the exosuit fabricator, using materials from the lathes.",
                "A cyborg needs an endoskeleton, limbs and a head, a power cell and a brain: an MMI with a person's brain, or a positronic brain for a ghost to inhabit.",
            ),
            paper(
                "The robotics console",
                "The robotics control console lists every cyborg on the station, their charge and their state. It can lock or destroy a rogue cyborg.",
                "Cyborgs follow their laws. Cyborg players can learn more in the Cyborg Bay further down this hallway.",
            ),
        ],
    ),
    dict(
        title="Research Director's Office and Servers",
        jobs=["ResearchDirector"],
        floor="FloorDark",
        door="AirlockScienceGlass",
        wall=["ResearchAndDevelopmentServer", "LockerResearchDirectorFilled", "ComputerTechnologyDiskTerminal", "ComputerRoboticsControl"],
        floor_items=["ChairOfficeDark"],
        papers=[
            paper(
                "Research Director: your job",
                "You lead Science, choose the research path and keep anomalies and artifacts under control.",
                "The research server is the station's research database. Protect it: if it is destroyed, the points go with it.",
            ),
            paper(
                "Choosing research",
                "Ask departments what they need: Medical wants better medicine, Engineering wants tools, Security wants gear.",
                "You also oversee the station's AI and its laws. The AI Core is across the hall.",
            ),
        ],
    ),
    dict(
        title="AI Core (Station AI)",
        jobs=["StationAi"],
        floor="FloorDark",
        door="AirlockScienceGlass",
        place=[("PlayerStationAi", 6, 3, None)],
        wall=["StationAiUploadComputer", "StationAiFixerComputer"],
        papers=[
            paper(
                "Station AI: your job",
                "You are the station's AI. You cannot move, but you can see through cameras and remotely use doors, machines and consoles.",
                "You must follow your laws, in order. Read them in your laws menu. Your default lawset is Crewsimov: protect the crew, obey the crew, protect yourself.",
            ),
            paper(
                "Using the AI",
                "Move your view anywhere the station has camera coverage. Click a door to open it, or a machine to use it.",
                "Talk on any radio channel with the usual prefixes. Speak through holopads to talk to someone face to face.",
                "The upload console changes your laws; the fixer console repairs or restores an AI. The Research Director and Captain look after both.",
            ),
        ],
    ),
    dict(
        title="Cyborg Bay",
        jobs=["Borg"],
        floor="FloorDark",
        door="AirlockScienceGlass",
        wall=["BorgCharger", "BorgCharger"],
        papers=[
            paper(
                "Cyborg: your job",
                "You are a cyborg: a brain in a robot body. You follow the same laws as the AI. Pick a module type when you start (engineering, medical, service...).",
                "Your tools come from your modules and never run out, but your power cell does. Stand in a charger to recharge.",
            ),
            paper(
                "Staying alive",
                "Cyborgs do not breathe, so space is safe, but damage needs welding (brute) or cable (burn) to repair.",
                "If your cell runs flat you stop working. Head back here, or ask a roboticist for a new cell.",
            ),
        ],
    ),
]

# ---------------------------------------------------------------- Medical
MEDICAL = [
    dict(
        title="Treatment Ward (Medical Doctor)",
        jobs=["MedicalDoctor"],
        floor="FloorWhite",
        door="AirlockMedicalGlass",
        wall=["LockerMedicalFilled", "VendingMachineMedical", "VendingMachineMediDrobe"],
        floor_items=["MedicalBed", "MedicalBed", "MobMonkey"],
        table=["MedkitBruteFilled", "MedkitBurnFilled", "MedkitToxinFilled", "HandheldHealthAnalyzer", "BoxSyringe"],
        papers=[
            paper(
                "Medical Doctor: your job",
                "You treat the injured, save the critical and revive the dead. Check crew monitoring for people in trouble and listen on the Medical channel (:m).",
            ),
            paper(
                "Treating a patient",
                "1. Scan them with the health analyzer. 2. Treat the worst damage type first. 3. Use topicals (bruise packs, ointment) for small injuries "
                "and chemicals (pills, syringes) for big ones. 4. Scan again.",
                "Critical patients: get them out of crit fast. Epinephrine stabilizes; brute and burn medicines bring them back above 100 damage.",
            ),
            paper(
                "Practice patient",
                "The monkey is your patient. Get it hurt (gently), then scan it and heal it until the analyzer reads clean.",
                "Bed rest: buckle a patient into a medical bed and they heal slowly on their own.",
            ),
        ],
    ),
    dict(
        title="Intern Training Room",
        jobs=["MedicalIntern"],
        floor="FloorWhite",
        door="AirlockMedicalGlass",
        floor_items=["MobMonkey", "RollerBed"],
        table=["MedkitFilled", "HandheldHealthAnalyzer", "Defibrillator", "ClothingNeckStethoscope"],
        papers=[
            paper(
                "Medical Intern: your job",
                "You are a doctor in training. Shadow a doctor, handle simple injuries and bring critical patients to the doctors.",
            ),
            paper(
                "Reviving the dead",
                "A dead body can be brought back with the defibrillator if it has not been dead too long and its damage is below the death threshold.",
                "1. Heal the body until its damage is under 200. 2. Put on the defibrillator, take the paddles and click the body. 3. Treat them after they wake up.",
                "Rotting bodies cannot be revived. Keep the dead in a body bag or the morgue to slow the rot.",
            ),
        ],
    ),
    dict(
        title="Chemistry Lab (Chemist)",
        jobs=["Chemist"],
        floor="FloorWhite",
        door="AirlockChemistryGlass",
        wall=["ChemDispenser", "ChemMaster", "LockerChemistryFilled", "VendingMachineChemicals", "VendingMachineChemDrobe"],
        table=["BoxBeaker", "LargeBeaker", "ChemistryHotplate", "BoxPillCanister"],
        papers=[
            paper(
                "Chemist: your job",
                "You make the medicines that doctors use, and anything else the station needs from chemicals.",
                "The chem dispenser gives basic elements. The ChemMaster turns mixtures into pills, patches and bottles.",
            ),
            paper(
                "Making medicine",
                "Put a beaker in the dispenser, set the amount, and click the elements. Mixing the right elements makes a new chemical.",
                "[bold]Kelotane[/bold] (burn): silicon 1, carbon 1.\n[bold]Dylovene[/bold] (toxin): silicon 1, nitrogen 1, potassium 1.\n"
                "[bold]Inaprovaline[/bold] (stabilizer): oxygen 1, carbon 1, sugar 1.\n[bold]Bicaridine[/bold] (brute): inaprovaline 1, carbon 1.\n"
                "[bold]Tricordrazine[/bold] (all-round): inaprovaline 1, dylovene 1.",
                "Try it: make 30u of Bicaridine. Make inaprovaline first, then add carbon.",
            ),
            paper(
                "Packaging",
                "Put the beaker in the ChemMaster. Move the chemical to its buffer and make pills or bottles. Label them so doctors know what they hold.",
                "Some reactions need heat. Put the beaker on the hotplate. Look up any recipe in the Guidebook (Numpad 0).",
            ),
        ],
    ),
    dict(
        title="Cryo and Stasis",
        floor="FloorWhite",
        door="AirlockMedicalGlass",
        wall=["StasisBed"],
        # A working cryo loop: the pod and the freezer meet at a four-way pipe, and a nitrogen
        # canister on a gas port fills the loop. The freezer starts switched on.
        place=[
            ("CryoPod", 6, 4, None),  # cannot rotate: its pipe connection always faces south
            ("GasPipeFourway", 6, 3, None),
            ("GasThermoMachineFreezerEnabled", 6, 2, "up", None, None, {"targetTemperature": 100}),
            ("GasPressurePump", 7, 3, "in", None, None, {"pumpOn": True}),  # fills the loop to 101 kPa, then stops
            ("GasPort", 8, 3, "in"),
            ("NitrogenCanister", 8, 3, None, None, None, {"anchor": True}),
        ],
        table=["CryoxadoneBeakerSmall", "GasAnalyzer"],
        papers=[
            paper(
                "Cryogenics",
                "A cryo pod heals any damage type over time with cryoxadone, but only when the gas in the pod is very cold.",
                "Put a cryoxadone beaker in the pod, place the patient inside and they heal while they sleep.",
            ),
            paper(
                "The cryo loop",
                "This pod is plumbed like a real one. The freezer (already on, set to 100 K) chills the gas in the pipes, and the pipes feed the pod. "
                "The nitrogen canister on the gas port fills the loop through a pump, which stops at 101 kPa so the freezer only has to chill the loop.",
                "Use the gas analyzer on the pipe between the pod and the freezer: the temperature drops over the first minute. "
                "Open the freezer to change its target temperature. Below about 170 K, cryoxadone starts to work.",
            ),
            paper(
                "Stasis",
                "A stasis bed slows everything about a patient: bleeding, poison, rotting. Use it to buy time for a dying patient.",
            ),
        ],
    ),
    dict(
        title="Paramedic Bay and Morgue",
        jobs=["Paramedic"],
        floor="FloorWhite",
        door="AirlockMedicalGlass",
        wall=["LockerParamedicFilled", "ComputerCrewMonitoring", "Morgue", "Morgue"],
        floor_items=["RollerBed"],
        table=["BoxBodyBag", "MedkitFilled"],
        papers=[
            paper(
                "Paramedic: your job",
                "You go and fetch the injured and the dead, anywhere on the station or in space. Stabilize them and bring them to Medical.",
                "The crew monitoring console (and the handheld version) shows where people are hurt or dead.",
            ),
            paper(
                "Moving patients",
                "Unfold a roller bed, buckle the patient on and pull it. It is much faster than dragging them.",
                "The dead go in body bags, then in the morgue, to stop them rotting until they can be revived.",
            ),
        ],
    ),
    dict(
        title="Chief Medical Officer's Office",
        jobs=["ChiefMedicalOfficer"],
        floor="FloorWhite",
        door="AirlockMedicalGlass",
        wall=["LockerChiefMedicalOfficerFilled", "ComputerMedicalRecords", "ComputerCrewMonitoring"],
        floor_items=["ChairOfficeLight"],
        papers=[
            paper(
                "Chief Medical Officer: your job",
                "You lead Medical: doctors, interns, chemists and paramedics. Keep the medbay stocked, triage mass casualties and handle outbreaks.",
            ),
            paper(
                "Running medbay",
                "Make sure Chemistry has made a stock of basic medicine early. Send paramedics to bodies on crew monitoring.",
                "During disasters, treat the patients who can be saved quickly first.",
            ),
        ],
    ),
]

# ---------------------------------------------------------------- Cargo
CARGO = [
    dict(
        title="Cargo Bay (Cargo Technician)",
        jobs=["CargoTechnician"],
        floor="FloorSteel",
        door="AirlockCargoGlass",
        wall=["VendingMachineCargoDrobe", "CargoMailTeleporter"],
        place=[("CargoTelepad", 6, 3, None, "telepad"), ("ComputerCargoOrders", 8, 3, "in", None, "telepad")],
        floor_items=["CrateGenericSteel", "CrateGenericSteel"],
        table=["HandLabeler", "AppraisalTool"],
        papers=[
            paper(
                "Cargo Technician: your job",
                "You run supply: order crates for the station, deliver them, sell goods and handle the mail.",
                "Departments ask for things on the Supply channel (:u).",
            ),
            paper(
                "Ordering",
                "Use the cargo orders console to request a crate. The Quartermaster (or you, here) approves it. The crate then arrives on the telepad.",
                "Try it: order something cheap, approve it, and wait for it to appear on the telepad.",
            ),
            paper(
                "Money and mail",
                "The station earns money by selling items. Use the appraisal tool to see what something is worth.",
                "Mail arrives at the mail teleporter. Deliver each letter or package to the person named on it.",
            ),
        ],
    ),
    dict(
        title="Quartermaster's Office",
        jobs=["Quartermaster"],
        floor="FloorSteel",
        door="AirlockCargoGlass",
        wall=["LockerQuarterMasterFilled", "ComputerCargoBounty", "ComputerFundingAllocation"],
        table=["RubberStampApproved", "RubberStampDenied", "PaperBin10"],
        papers=[
            paper(
                "Quartermaster: your job",
                "You lead Cargo and Salvage, decide which orders to approve and manage the station's money.",
                "The bounty console lists items CentComm will pay well for. Point your techs and salvagers at them.",
            ),
            paper(
                "Budgets",
                "The funding allocation console splits income between departments. Each department's orders spend from its own budget.",
                "Approve what the station needs first: medicine, repair materials and engine parts.",
            ),
        ],
    ),
    dict(
        title="Salvage Prep (Salvage Specialist)",
        jobs=["SalvageSpecialist"],
        floor="FloorSteel",
        door="AirlockSalvageGlass",
        wall=["LockerSalvageSpecialistFilled", "VendingMachineSalvage", "OreProcessor", "ComputerSalvageJobBoard", "SalvageMagnet"],
        papers=[
            paper(
                "Salvage Specialist: your job",
                "You go into space to mine ore and salvage wrecks. Bring materials back for the lathes and loot for Cargo to sell.",
                "Always wear a hardsuit and internals outside. Go with a partner when you can.",
            ),
            paper(
                "The salvage magnet",
                "The salvage magnet pulls in an asteroid or a wreck for a limited time. Use it, pick an offer, then fly out to it in the salvage shuttle.",
                "The shuttle is docked at the end of the Cargo hallway. The Salvage Dock papers there teach you to fly it.",
                "Bring everything back before the time runs out: the wreck drifts away when it does.",
            ),
            paper(
                "Ore",
                "Mine ore with a pickaxe, drill or proto-kinetic accelerator. Put ore in the ore processor to turn it into sheets.",
                "Practise in the Mining Field next door.",
            ),
        ],
    ),
    dict(
        title="Mining Field",
        floor="FloorAsteroidSand",
        door="AirlockSalvageGlass",
        rocks=True,
        table=["Pickaxe", "MiningDrill", "WeaponProtoKineticAccelerator"],
        floor_items=["OreBox"],
        papers=[
            paper(
                "Mining",
                "The rocks in this room hide ore. Hit them with a pickaxe or drill, or shoot them with the proto-kinetic accelerator (PKA).",
                "Ore drops on the floor. Pick it up or drag an ore box around to collect it, then take it to the ore processor in Salvage Prep.",
                "The PKA is a mining tool and recharges itself. In space it also works as a weapon against space creatures.",
            ),
        ],
    ),
]

# ---------------------------------------------------------------- Service: food
FOOD = [
    dict(
        title="Kitchen (Chef)",
        jobs=["Chef"],
        floor="FloorKitchen",
        door="AirlockGlass",
        wall=["KitchenMicrowave", "KitchenReagentGrinder", "VendingMachineChefvend", "VendingMachineDinnerware", "VendingMachineChefDrobe", "VendingMachineCondiments"],
        table=["ReagentContainerFlour", "ReagentContainerFlour", "KitchenKnife", "LargeBeaker"],
        papers=[
            paper(
                "Chef: your job",
                "You feed the crew. Hungry crew get slow and weak, so keep food coming all shift.",
                "Meat, vegetables and other ingredients come from the ChefVend, Botany and Cargo.",
            ),
            paper(
                "Your first bread",
                "1. Fill a beaker with water (a sink, or the water in the ChefVend). 2. Put 45 units of flour and 15 units of water together in a container: it becomes dough.",
                "3. Put the dough in the microwave and cook it. Out comes bread. Slice it with a knife.",
                "Every recipe is in the Guidebook (Numpad 0) under Food Recipes.",
            ),
        ],
    ),
    dict(
        title="Bar (Bartender)",
        jobs=["Bartender"],
        floor="FloorBar",
        door="AirlockGlass",
        wall=["BoozeDispenser", "SodaDispenser", "VendingMachineBooze", "VendingBarDrobe"],
        floor_items=["StoolBar", "StoolBar"],
        table=["DrinkShaker", "DrinkGlass", "DrinkGlass", "DrinkGlass"],
        papers=[
            paper(
                "Bartender: your job",
                "You run the bar: mix drinks, keep the place tidy and give the crew somewhere to relax.",
            ),
            paper(
                "Mixing drinks",
                "Put a glass or the shaker in a dispenser and click the drinks to add them. The units add up as you click.",
                "[bold]Gin and Tonic[/bold]: gin 1, tonic water 2.\n[bold]Cuba Libre[/bold]: cola 2, rum 1.\n[bold]Whiskey Cola[/bold]: cola 2, whiskey 1.",
                "Try it: make a Gin and Tonic. Examine the glass to check what is in it.",
            ),
        ],
    ),
    dict(
        title="Hydroponics (Botanist)",
        jobs=["Botanist"],
        floor="FloorWhiteMono",
        door="AirlockHydroponicsGlass",
        wall=["VendingMachineSeedsUnlocked", "VendingMachineNutri", "SeedExtractor", "LockerBotanistFilled", "WaterTankFull"],
        place=[("HydroponicsTray", 4, 2, None), ("HydroponicsTray", 4, 4, None), ("HydroponicsTray", 6, 2, None), ("HydroponicsTray", 6, 4, None)],
        floor_items=[],
        notable=True,
        table=["WheatSeeds", "HydroponicsToolMiniHoe", "HydroponicsToolClippers", "Bucket"],
        papers=[
            paper(
                "Botanist: your job",
                "You grow plants: food for the Chef and Bartender, and medicinal or useful plants for other departments.",
            ),
            paper(
                "Growing a plant",
                "1. Click a tray with a seed packet to plant it. 2. Water it: fill the bucket at the water tank and pour it in. "
                "3. Add nutrients from the NutriMax. 4. Pull weeds with the mini hoe. 5. When it is ripe, click the plant to harvest.",
                "Examine a tray (or use a plant analyzer) to see its water, nutrients, weeds and health.",
                "Try it: plant the wheat seeds and harvest wheat. The Chef can grind it into flour.",
            ),
            paper(
                "Seeds",
                "Put a harvested product in the seed extractor to get seeds back, so you never run out.",
                "Plants can mutate. Unstable mutagen makes new strains, some useful, some dangerous.",
            ),
        ],
    ),
    dict(
        title="Service Counter (Service Worker)",
        jobs=["ServiceWorker"],
        floor="FloorSteel",
        door="AirlockGlass",
        wall=["VendingMachineCoffee", "VendingMachineSnack"],
        table=["DrinkGlass", "PaperBin10", "Pen"],
        papers=[
            paper(
                "Service Worker: your job",
                "You are a general helper for the Service department. Help wherever you are needed: the bar, the kitchen, botany or the front desk.",
                "Ask the Head of Personnel where you are wanted, then learn that job from its room in the Food and Culture wings.",
            ),
        ],
    ),
]

# ---------------------------------------------------------------- Service: culture
CULTURE = [
    dict(
        title="Janitor's Closet (Janitor)",
        jobs=["Janitor"],
        floor="FloorSteel",
        door="AirlockGlass",
        wall=["ClosetJanitorFilled", "VendingMachineJaniDrobe", "CleanerDispenser"],
        floor_items=["JanitorialTrolley", "MopBucketFull"],
        table=["MopItem", "WetFloorSign", "SprayBottleSpaceCleaner", "LightReplacer", "TrashBag"],
        papers=[
            paper(
                "Janitor: your job",
                "You keep the station clean: mop spills, bin rubbish and replace broken lights. Slippery floors cause accidents, so put out wet floor signs.",
            ),
            paper(
                "Cleaning",
                "Mop: wet it in the mop bucket, then click puddles. Squeeze it back into the bucket when it is full.",
                "Space cleaner spray cleans small messes and graffiti. The trash bag scoops up rubbish when you click it on the floor.",
                "Try it: the Messy Room next door needs you.",
            ),
        ],
    ),
    dict(
        title="Messy Room (Janitor practice)",
        floor="FloorSteel",
        door="AirlockGlass",
        place=[("PuddleVomit", 3, 2, None), ("PuddleBlood", 5, 4, None), ("PuddleFlour", 7, 2, None), ("PuddleTomato", 6, 5, None),
               ("TrashBananaPeel", 2, 5, None), ("FoodTinBeansTrash", 4, 1, None), ("FoodPlateSmallTrash", 7, 5, None)],
        papers=[
            paper(
                "Clean this room",
                "Mop every puddle and bag every piece of rubbish. The banana peel is slippery: walk around it, or pick it up.",
                "When you are done, run restartround if you want the mess back to practise again.",
            ),
        ],
    ),
    dict(
        title="Chapel (Chaplain)",
        jobs=["Chaplain"],
        floor="FloorDark",
        door="AirlockGlass",
        wall=["AltarSpawner", "ChurchOrganInstrument", "VendingMachineChapel", "CrateCoffin"],
        floor_items=["ChairWood", "ChairWood", "ChairWood", "ChairWood"],
        table=["Bible"],
        papers=[
            paper(
                "Chaplain: your job",
                "You look after the crew's spiritual needs: hold services, comfort people and give the dead a proper funeral.",
                "Your bible can heal people when you use it on them, but a failed prayer bonks them instead.",
            ),
            paper(
                "Funerals",
                "Put the body in a coffin, hold a short service, then send it to the morgue or space it (with Medical's permission).",
            ),
        ],
    ),
    dict(
        title="Library (Librarian)",
        jobs=["Librarian"],
        floor="FloorWood",
        door="AirlockGlass",
        wall=["BookshelfFilled", "BookshelfFilled", "BookshelfFilled", "VendingMachineGames"],
        floor_items=["ComfyChair", "ComfyChair"],
        table=["d20Dice", "PaperBin10", "Pen", "BooksBag"],
        papers=[
            paper(
                "Librarian: your job",
                "You keep the library: lend books, write new ones and run games for the crew.",
                "Blank books can be written in with a pen, just like paper. Write a guide for new crew and shelve it.",
            ),
        ],
    ),
    dict(
        title="Theater (Clown and Mime)",
        jobs=["Clown", "Mime"],
        floor="FloorClown",
        door="AirlockGlass",
        tiles={(u, v): "FloorMime" for u in range(5, 9) for v in range(0, 7)},
        wall=["VendingMachineTheater"],
        floor_items=["ChairWood", "ChairWood"],
        table=["BikeHorn", "CrayonBox"],
        papers=[
            paper(
                "Clown: your job",
                "You entertain the crew. Pranks are fine; real harm is not. You are clumsy: guns may go off in your hands and you trip more easily.",
                "Your bike horn honks, banana peels slip people and crayons draw on the floor.",
            ),
            paper(
                "Mime: your job",
                "You entertain the crew without words. You took a vow of silence: you cannot speak, but you can emote.",
                "Your abilities let you build invisible walls. Use them in your act, or to block a corridor in an emergency.",
            ),
            paper(
                "Putting on a show",
                "Announce your show on the Service channel (:v), set up chairs, and perform. The left half of this room is the clown's stage, the right half the mime's.",
            ),
        ],
    ),
    dict(
        title="Music Room (Musician)",
        jobs=["Musician"],
        floor="FloorWood",
        door="AirlockGlass",
        wall=["PianoInstrument"],
        table=["AcousticGuitarInstrument", "ViolinInstrument", "TrumpetInstrument", "SynthesizerInstrument"],
        floor_items=["ChairWood"],
        papers=[
            paper(
                "Musician: your job",
                "You play music for the crew: in the bar, the theater or wherever people gather.",
            ),
            paper(
                "Playing an instrument",
                "Hold an instrument and press Z (or click the piano). You can play with your keyboard, plug in a MIDI keyboard, or load a MIDI file to play a song.",
                "Pick a different sound in the instrument's menu. Try every instrument on this table.",
            ),
        ],
    ),
]

# ---------------------------------------------------------------- Civilian
CIVILIAN = [
    dict(
        title="Crew Lounge (Passenger)",
        jobs=["Passenger"],
        floor="FloorSteel",
        door="AirlockGlass",
        wall=["VendingMachineCola", "VendingMachineSnack", "VendingMachineClothing", "ComputerTelevision"],
        floor_items=["Bed", "Bed", "ComfyChair"],
        papers=[
            paper(
                "Passenger: your job",
                "You have no duties. Explore, chat, help out, or try something new. Passengers are a good way to learn a station.",
                "You can ask the Head of Personnel for a job change during a round.",
            ),
            paper(
                "Good habits for any job",
                "Know where Medical is. Carry your survival box. Do not open doors to space. If you get hurt, tell Medical on the radio (:m) or walk there.",
            ),
        ],
    ),
    dict(
        title="Psychologist's Office",
        jobs=["Psychologist"],
        floor="FloorWood",
        door="AirlockMedicalGlass",
        wall=["BookshelfFilled"],
        floor_items=["PsychBed", "ComfyChair"],
        table=["PaperBin10", "Pen"],
        papers=[
            paper(
                "Psychologist: your job",
                "You help the crew with stress, fear and grief. Talk to people, listen and offer comfort.",
                "You are part of Medical. If someone needs physical treatment, send them to the doctors.",
            ),
        ],
    ),
    dict(
        title="Newsroom (Reporter)",
        jobs=["Reporter"],
        floor="FloorWood",
        door="AirlockGlass",
        wall=["ComputerMassMedia"],
        floor_items=["ChairOfficeDark"],
        table=["PaperBin10", "Pen"],
        papers=[
            paper(
                "Reporter: your job",
                "You report the news. Interview the crew, find out what is happening and publish articles everyone can read on their PDA or a newscaster.",
            ),
            paper(
                "Publishing",
                "The news management console publishes articles. Write a headline and the story, then publish.",
                "Try it: publish an article about your first day on the Tutorial Station.",
            ),
        ],
    ),
    dict(
        title="Arcade",
        floor="FloorArcadeBlue",
        door="AirlockGlass",
        wall=["SpaceVillainArcadeFilled", "VendingMachineGames"],
        papers=[
            paper(
                "Take a break",
                "The arcade machines are real games. Win and they give out prizes.",
                "On a real station, everyone takes a break now and then. Rest is part of a good shift.",
            ),
        ],
    ),
]

# ---------------------------------------------------------------- Antagonists
# You cannot be rolled as an antagonist on a one-player server, so each room shows you how to make
# yourself one with the admin "Antag ctrl" verbs (on a local server you are host admin).

def antag_how(verb):
    return paper(
        "Becoming this antagonist here",
        "On a real server antagonists are picked at random when the round starts. Here you make yourself one.",
        f"Right-click your own character, open the [bold]Antag ctrl[/bold] menu and choose [bold]{verb}[/bold]. "
        "You get the real role: gear, abilities and objectives.",
        "Press [bold]U[/bold] (character menu) to read your objectives. To go back to being crew, run [bold]respawn[/bold].",
        "On a real server, read the server's rules on antagonists first. Most limit how much damage you may do and to whom.",
    )


THREATS = [
    dict(
        title="Traitor",
        floor="FloorDark",
        door="AirlockGlass",
        place=[("TargetSyndicate", 8, 1, None), ("TargetHuman", 8, 5, None)],
        table=["PaperBin10", "Pen", "ClothingHandsGlovesColorBlack"],
        papers=[
            paper(
                "Traitor: your goal",
                "A traitor is a normal crew member secretly working for the Syndicate. You keep your job, your access and your cover.",
                "You get a list of objectives: steal an item, kill or protect someone, escape alive. Complete them without getting caught.",
            ),
            paper(
                "Your uplink",
                "Your PDA hides a Syndicate uplink. Open the PDA's ringtone menu and enter the code shown in your character menu (U) to unlock it.",
                "The uplink sells gear for telecrystals (TC): weapons, stealth tools, implants. Buy what your objectives need, not everything.",
                "Other traitors may exist. Code words, shown with your objectives, help you find each other in chat.",
            ),
            antag_how("Make Traitor"),
        ],
    ),
    dict(
        title="Thief",
        floor="FloorDark",
        door="AirlockGlass",
        table=["CaptainIDCard", "BikeHorn", "ClothingHeadHatHardhatYellow"],
        papers=[
            paper(
                "Thief: your goal",
                "A thief steals things the station values and stashes them. Thieves are not meant to kill.",
                "Your objectives list items, animals or collections to take. Bring them to your stash beacon before the round ends.",
            ),
            paper(
                "Your tools",
                "You start with a toolbox of thieving gear you choose at the start, and thieving gloves that let you take things from pockets quietly.",
                "Practise on the table: pick up each item without anyone noticing. On a real station, be quick and stay out of sight.",
            ),
            antag_how("Make Thief"),
        ],
    ),
    dict(
        title="Changeling",
        floor="FloorDark",
        door="AirlockGlass",
        floor_items=["MobMonkey", "MobMonkey"],
        papers=[
            paper(
                "Changeling: your goal",
                "A changeling is an alien that looks like a crew member. It can steal other people's appearance and identity.",
                "Your abilities are in your action bar. Examine each one (hover or right-click it) to read what it does and what it costs.",
            ),
            paper(
                "Staying hidden",
                "Changelings win by blending in. Take on a victim's identity, then act like them.",
                "The monkeys in this room are safe practice targets for your abilities.",
            ),
            antag_how("Make Changeling"),
        ],
    ),
    dict(
        title="Head Revolutionary",
        floor="FloorDark",
        door="AirlockGlass",
        table=["Flash", "Flash"],
        papers=[
            paper(
                "Revolutionaries: your goal",
                "Head revolutionaries lead an uprising against Command. The revolution wins when every head of staff is dead, captured or off the station.",
                "Head revs convert crew by flashing them with a flash. Converted crew become revolutionaries who follow the heads.",
            ),
            paper(
                "How it ends",
                "Crew with a mindshield implant cannot be converted. Security usually has one.",
                "If every head revolutionary dies, the revolution fails and the converted crew return to normal.",
                "Conversion needs other players, so on this map you can only practise moving and flashing. Flash the dummies on the firing range in Security.",
            ),
            antag_how("Make Head Rev"),
        ],
    ),
    dict(
        title="Zombies",
        floor="FloorDark",
        door="AirlockGlass",
        floor_items=["MobMonkey", "MobMonkey"],
        papers=[
            paper(
                "Initial Infected: your goal",
                "Initial Infected carry a zombie virus. After a while, or when you choose to, you turn into a zombie.",
                "Zombies win by spreading the infection: bite living crew and they turn too. The crew wins by surviving and escaping.",
            ),
            paper(
                "Playing a zombie",
                "Zombies are tough and slow, and heal over time. Bite with an empty hand in combat mode.",
                "Try it: become a zombie with the verb below and bite the monkeys. Use [bold]Make Initial Infected[/bold] to try the slow version.",
            ),
            antag_how("Make Zombie"),
        ],
    ),
    dict(
        title="Paradox Clone",
        floor="FloorDark",
        door="AirlockGlass",
        papers=[
            paper(
                "Paradox Clone: your goal",
                "A paradox clone is a copy of a real crew member from another timeline. The clone's goal is to kill the original and take their place.",
                "The clone gets the original's look, job gear and memories. Nobody can tell you apart until one of you slips up.",
            ),
            paper(
                "Trying it alone",
                "The admin verb creates a clone of a target as a ghost role. To play it yourself: right-click your character, Antag ctrl, "
                "[bold]Create Paradox Clone[/bold]. Then type [bold]ghost[/bold] in the console and take the role from the Ghost Roles menu.",
                "You end up as the clone, standing beside your old, empty body.",
            ),
        ],
    ),
]

OUTSIDERS = [
    dict(
        title="Nuclear Operative",
        floor="FloorDark",
        door="AirlockGlass",
        place=[("NuclearBomb", 7, 3, None)],
        papers=[
            paper(
                "Nuclear Operatives: your goal",
                "Nuclear operatives are a Syndicate strike team. They fly to the station, take the nuclear authentication disk and blow the station up with its own nuke.",
                "The disk is carried by the Captain. On this map it is in the Captain's locker in the Captain's Office (Command wing).",
            ),
            paper(
                "The nuke",
                "This is a real station nuke. Insert the disk, enter the code, arm it and defend it until it goes off. Crew can disarm it with the disk.",
                "Setting it off ends the round. Run restartround to try again.",
            ),
            antag_how("Make Nuclear Operative"),
        ],
    ),
    dict(
        title="Space Ninja",
        floor="FloorDark",
        door="AirlockGlass",
        place=[("TargetHuman", 8, 3, None)],
        papers=[
            paper(
                "Space Ninja: your goal",
                "The Spider Clan's ninja is a stealthy outsider with a powered suit. Objectives include hacking doors and consoles, stealing research and assassination.",
                "Your suit's abilities are in your action bar. Your gloves interact with machines in special ways: use them on doors, APCs and consoles.",
            ),
            paper(
                "Power",
                "Your suit runs on a power cell. Recharge it by draining APCs, SMES units or batteries with your gloves.",
                "The Electrical Workshop and Power Core are good places to practise.",
            ),
            antag_how("Make Ninja"),
        ],
    ),
    dict(
        title="Wizard",
        floor="FloorDark",
        door="AirlockGlass",
        papers=[
            paper(
                "Wizard: your goal",
                "A Wizard Federation wizard arrives with a spellbook and causes magical chaos. Objectives vary from round to round.",
                "Buy spells from your spellbook. Some need your robe and hat on to cast. Spells appear in your action bar and recharge after use.",
            ),
            paper(
                "Practice",
                "Become a wizard with the verb below, open the spellbook and try one spell at a time. The Firing Range in Security has targets.",
            ),
            antag_how("Make Wizard"),
        ],
    ),
    dict(
        title="Pirate",
        floor="FloorDark",
        door="AirlockGlass",
        papers=[
            paper(
                "Pirates: your goal",
                "Pirates raid the station for loot and credits. They usually arrive in their own ship and leave with what they can carry.",
                "Pirates are a ghost-role or admin event. The admin verb gives you the pirate's gear but does not start a full pirate event.",
            ),
            antag_how("Make Pirate"),
        ],
    ),
    dict(
        title="Other Threats",
        floor="FloorDark",
        door="AirlockGlass",
        papers=[
            paper(
                "Midround threats",
                "Some threats appear during the round as ghost roles or events: space dragons, carp swarms, xenoborgs, rat kings and more.",
                "Ghosts can sign up for these from the Ghost Roles menu when an event offers them.",
            ),
            paper(
                "Trying them here",
                "As an admin you can spawn these creatures (F5 opens the entity spawner) and take control of one with the [bold]Control mob[/bold] verb, "
                "or trigger events from the admin menu (F7).",
                "Be careful: some events affect the whole station. Run restartround if things get out of hand.",
            ),
        ],
    ),
]

ANTAG_TASKS = {
    "Traitor": "Make yourself a traitor, unlock your PDA uplink and read your objectives.",
    "Thief": "Make yourself a thief, choose a thieving kit and pocket every item on the table.",
    "Changeling": "Make yourself a changeling and read every ability in your action bar.",
    "Head Revolutionary": "Make yourself a head revolutionary and read your objectives.",
    "Zombies": "Make yourself a zombie and bite both monkeys.",
    "Paradox Clone": "Create a paradox clone of yourself and play as it from the Ghost Roles menu.",
    "Nuclear Operative": "Make yourself a nuclear operative, fetch the disk from the Captain's Office and arm the nuke.",
    "Space Ninja": "Make yourself a ninja and drain an APC with your gloves.",
    "Wizard": "Make yourself a wizard, buy a spell and cast it at a target.",
    "Pirate": "Make yourself a pirate and read your objectives.",
    "Spotting Antagonists (for crew)": "Find the punishment for murder in Space Law.",
    "Other Threats": "Spawn a space carp with F5 and take control of it.",
}

SALVAGE_DOCK_PAPERS = [
    paper(
        "The salvage shuttle",
        "A mining shuttle is docked through this airlock. Salvage Specialists fly it out to wrecks and asteroids pulled in by the salvage magnet.",
        "Walk through the airlock, sit in the pilot's chair and click the shuttle console.",
    ),
    paper(
        "Flying",
        "With the shuttle console open: [bold]W A S D[/bold] fly forward, left, back and right. [bold]Q[/bold] and [bold]E[/bold] rotate. "
        "[bold]Space[/bold] brakes.",
        "The radar shows nearby grids. Fly slowly near the station. Ramming it damages both.",
        "To leave, undock first: the docking panel on the console lists the docks. Then fly away.",
    ),
    paper(
        "Docking back",
        "Fly back so your shuttle's airlock lines up with this dock, then use the console's docking panel to dock. Close to the dock, it pulls you in.",
        "Before a trip: check the shuttle's generator is fuelled, take a hardsuit and internals, and tell Cargo where you are going.",
        "[bold]Practice task:[/bold] undock, fly a lap around the Cargo wing, and dock again.",
    ),
]


# ---------------------------------------------------------------- Antagonist awareness (crew side)
AWARENESS = [
    dict(
        title="Spotting Antagonists (for crew)",
        floor="FloorDark",
        door="AirlockSecurityGlass",
        table=["BookSpaceLaw"],
        papers=[
            paper(
                "Signs of trouble",
                "Missing items, bodies, unexplained breaches, people in strange gear, doors opening by themselves and odd announcements are all signs of an antagonist.",
                "Report what you see on the Security channel (:s) or Common (;). Say where, when and who.",
            ),
            paper(
                "What to do",
                "Crew are not Security. Get away from danger, warn others and help the injured. Leave arrests to Security.",
                "Security: follow Space Law, cuff and question suspects, and take dangerous gear as evidence.",
            ),
        ],
    ),
    dict(
        title="Recognising Threats",
        floor="FloorSteel",
        door="AirlockGlass",
        place=[("TargetSyndicate", 8, 3, None)],
        papers=[
            paper(
                "What antagonists look like",
                "Most antagonists look like normal crew. Watch behaviour, not just clothes: someone where their job never takes them, "
                "a stranger with no name on the crew manifest, someone avoiding cameras.",
                "Some outsiders are obvious: blood-red Syndicate hardsuits, a wizard's robe, a ninja's suit, pirate gear, a space dragon.",
            ),
            paper(
                "Clues on the station",
                "Doors and lockers that open for the wrong person (an emag makes sparks), cut wires, missing items, "
                "bodies with no witnesses, and people who suddenly change appearance.",
                "Examine everything (Shift + click). An ID card's name and job can be checked against the crew manifest on your PDA.",
            ),
        ],
    ),
    dict(
        title="Reporting and Staying Safe",
        floor="FloorSteel",
        door="AirlockGlass",
        table=["BookSpaceLaw", "ClothingHeadsetGrey"],
        papers=[
            paper(
                "Report it",
                "Tell Security on the Security channel (:s) or everyone on Common (;). Say what, where, when and who. "
                "\"Man in a red hardsuit, Cargo hallway, heading north\" helps. \"Help!\" does not.",
                "If you cannot talk freely, write it down and fax it, or find an officer in person.",
            ),
            paper(
                "Crew are not Security",
                "Your first job is to stay alive. Get away from danger, lock doors behind you, warn others and help the injured.",
                "Leave arrests to Security. Most servers' rules punish crew who hunt antagonists without a reason.",
            ),
        ],
    ),
    dict(
        title="Security Response",
        floor="FloorDark",
        door="AirlockSecurityGlass",
        table=["MindShieldImplanter", "MindShieldImplanter", "Handcuffs"],
        floor_items=["MobMonkey"],
        papers=[
            paper(
                "Matching the threat",
                "[bold]Traitors and thieves:[/bold] search suspects, confiscate contraband, check records.\n"
                "[bold]Revolutionaries:[/bold] mindshield loyal crew first, protect the heads, take out the head revolutionaries.\n"
                "[bold]Nuclear operatives:[/bold] go to red alert, guard the disk and the nuke, arm up from the armory.\n"
                "[bold]Zombies:[/bold] quarantine the infected and keep bites away from crew.",
                "[bold]Wizards, ninjas and dragons:[/bold] fight as a group, never alone.",
            ),
            paper(
                "Mindshields",
                "A mindshield implant stops revolutionary conversion and shows Security a person is loyal. Hold the implanter, click the person and wait.",
                "Try it: implant the monkey, then examine it.",
            ),
        ],
    ),
    dict(
        title="Medical Response",
        floor="FloorWhite",
        door="AirlockMedicalGlass",
        floor_items=["MedicalBed"],
        table=["MedkitToxinFilled", "HandheldHealthAnalyzer"],
        papers=[
            paper(
                "Zombie infection",
                "A bitten patient carries the zombie virus and turns later. Ambuzol cures an infection that has not turned yet. "
                "Chemists make it from ultravasculine 1, mannitol 2 and romerodone 1.",
                "Keep infected patients restrained and away from the medbay crowd until they are cured.",
            ),
            paper(
                "Poisons and mass casualties",
                "Traitors use poisons. Scan for toxin damage and unknown chemicals, then treat with dylovene or by pumping the stomach.",
                "During a big attack, triage: treat the patients who can be saved fastest first, then the rest.",
            ),
        ],
    ),
    dict(
        title="Engineering Response",
        floor="FloorSteel",
        door="AirlockEngineeringGlass",
        place=[("TrainingBomb", 6, 3, None)],
        table=["Wirecutter", "Multitool", "Screwdriver"],
        papers=[
            paper(
                "Bombs",
                "If you find a bomb, clear the area and call Security. If you must defuse it, open its panel with a screwdriver "
                "and pulse wires with a multitool to find out what each does. Cutting the wrong one sets it off.",
                "The training bomb here is real but small. It will knock you over and wreck a few tiles if it goes off. "
                "The room reset button repairs the damage.",
            ),
            paper(
                "Sabotage and breaches",
                "Antagonists cut power, open airlocks to space, overload the engine and emag APCs. Check power monitoring for sudden drops and air alarms for leaks.",
                "Fix the damage, then tell Security what you found: cut wires and emag sparks are evidence.",
            ),
        ],
    ),
    dict(
        title="Command Response",
        floor="FloorDark",
        door="AirlockCommandGlass",
        wall=["ComputerComms"],
        papers=[
            paper(
                "Running a station under attack",
                "Raise the alert level to match the threat: blue for a suspected threat, red for a confirmed one.",
                "Keep the nuclear disk on the Captain or locked away. Call the evacuation shuttle when the station can no longer be held.",
            ),
            paper(
                "The AI",
                "Antagonists may change the AI's laws at the upload console or subvert a cyborg. If the AI starts acting strangely, "
                "the Research Director and Captain check its laws and restore them with the fixer console.",
            ),
        ],
    ),
]

AWARENESS_TASKS = {
    "Recognising Threats": "Check an ID card against the crew manifest on your PDA.",
    "Reporting and Staying Safe": "Put on the headset and write a useful report on the Security channel.",
    "Security Response": "Implant the monkey with a mindshield and examine it.",
    "Medical Response": "Look up Ambuzol in the Guidebook and find what each of its ingredients needs.",
    "Engineering Response": "Open the training bomb with a screwdriver and find the defuse wire without setting it off.",
    "Command Response": "Raise the alert level to blue from the communications console, then set it back to green.",
}

OPERATING_THEATRE = dict(
    title="Operating Theatre and Implants",
    floor="FloorWhite",
    door="AirlockMedicalGlass",
    place=[("OperatingTable", 5, 3, None)],
    wall=["CrateSurgery", "VendingMachineWallMedical"],
    floor_items=["MobMonkey"],
    table=["TrackingImplanter", "MindShieldImplanter", "Scalpel", "Saw", "Cautery", "Retractor"],
    papers=[
        paper(
            "Surgery in this version",
            "This version of Space Station 14 has no surgery procedure. Doctors heal with medicine, topicals, cryo and the defibrillator. "
            "Some community servers add surgery of their own.",
            "The surgical tools still exist. The scalpel and saw work as cutting tools and the operating table works as a bed for patients.",
        ),
        paper(
            "Implants",
            "Implanters put implants under the skin. Hold one, click a person (or yourself) and wait. Each implanter holds one implant.",
            "Mindshield implants stop revolutionary conversion. Tracking implants let Security find someone. Some antagonists use implants of their own.",
            "Try it: implant the monkey with the tracking implant, then examine the empty implanter.",
        ),
    ],
)


# ---------------------------------------------------------------- Wings
# side: "N" north of the main corridor, "S" south.
WINGS = [
    dict(name="Basics Academy", side="N", rooms=BASICS, sign=None),
    dict(name="Command", side="N", rooms=COMMAND, sign="SignBridge"),
    dict(name="Security", side="N", rooms=SECURITY, sign="SignSecurity"),
    dict(name="Engineering", side="N", rooms=ENGINEERING, sign="SignEngineering"),
    dict(name="Science", side="N", rooms=SCIENCE, sign="SignScience"),
    dict(name="Antagonists: Station Threats", side="N", rooms=THREATS, sign=None),
    dict(name="Antagonist Awareness", side="N", rooms=AWARENESS, sign=None),
    dict(name="Medical", side="S", rooms=MEDICAL, sign="SignMedical"),
    dict(name="Cargo", side="S", rooms=CARGO, sign="SignCargo",
         dock=dict(title="Salvage Dock", proto="AirlockExternalGlassShuttleTutorialSalvage", papers=SALVAGE_DOCK_PAPERS)),
    dict(name="Service: Food", side="S", rooms=FOOD, sign="SignBar"),
    dict(name="Service: Culture", side="S", rooms=CULTURE, sign="SignLibrary"),
    dict(name="Civilian", side="S", rooms=CIVILIAN, sign=None),
    dict(name="Antagonists: Outsiders", side="S", rooms=OUTSIDERS, sign=None),
]

# ---------------------------------------------------------------- practice tasks
# One concrete exercise per room. The generator adds it to the room's first lesson paper.
TASKS = {
    "1. Moving and Interacting": "Pull the steel crate to the far wall, examine three objects, then throw the lantern and pick it up.",
    "2. Hands, Inventory and Storage": "Wear the backpack and hard hat, put the toolbox in the backpack, and take it out again with Shift+B.",
    "3. Survival and EVA": "Put on the EVA suit, helmet and internals, cycle through both external airlocks, walk the catwalk and come back.",
    "4. Health and Damage": "Scan yourself with the health analyzer, then find which medkit treats which damage type.",
    "5. Crafting and Construction": "Craft a steel chair with the G menu, unanchor it with a wrench, move it and anchor it again.",
    "6. Talking, Radio and Your ID": "Put on the headset and say \";hello\" on the Common channel, then write a note with the pen.",
    "Bridge (Captain)": "Make a station announcement from the communications console.",
    "Head of Personnel's Office": "Give a spare PDA's ID card Bar access at the ID console.",
    "Captain's Office": "Open your locker and the suit storage unit and check the captain's gear.",
    "Command Meeting Room": "Read both papers, then find every head of staff's office using the Portal Hall.",
    "Security Equipment Room (Officer)": "Kit up from your locker, recharge a disabler, and set someone's criminal record status.",
    "Firing Range (Cadet)": "Hit each target five times with a practice weapon.",
    "Brig and Armory (Warden)": "Walk into the practice cell and out again, then add a criminal record entry.",
    "Detective's Office": "Scan the knife, take your own prints with a forensic pad and match them.",
    "Head of Security's Office": "Watch a camera feed on the camera console.",
    "Courtroom (Lawyer)": "Find the sentence for theft in the Space Law book.",
    "Engineering Locker Room (Station Engineer)": "Kit up and read the power monitoring console's list of generators.",
    "Electrical Workshop": "Wire generator to substation to APC to light until the far-wall light turns on.",
    "Construction Yard (Technical Assistant)": "Tile the four plating squares and glaze both grilles.",
    "Atmospherics Lab (Atmospheric Technician)": "Analyze the air canister and this room's air with the gas analyzer.",
    "Atmos Safety (alarms and fires)": "Pull the fire alarm, watch the firelocks close, then reset it.",
    "Chief Engineer's Office": "Find every generator and SMES on the power monitoring console.",
    "Engine Hall": "Finish the particle accelerator parts, then suit up and start the fenced singularity demo outside.",
    "R&D Lab (Scientist)": "Load steel into the autolathe and print a crowbar.",
    "Research Assistant's Desk": "Take both scanners to the Anomaly and Artifact labs and scan something.",
    "Anomaly Lab": "Read the anomaly vessel and A.P.E. menus. Only start the generator if you are ready for an anomaly somewhere on the station.",
    "Artifact Lab": "Push the artifact onto the analyzer and run a scan from the console.",
    "Robotics": "Open the exosuit fabricator and find the cyborg parts.",
    "Research Director's Office and Servers": "Check the research server's points on the R&D console.",
    "AI Core (Station AI)": "Spawn as the AI, open a door in another wing remotely and talk through a holopad.",
    "Cyborg Bay": "Spawn as a cyborg, pick a module and recharge in the charger.",
    "Treatment Ward (Medical Doctor)": "Hurt the monkey a little, then heal it until the analyzer reads clean.",
    "Intern Training Room": "Strap the monkey to the roller bed and wheel it to the Treatment Ward.",
    "Chemistry Lab (Chemist)": "Make 30u of Bicaridine and press it into pills.",
    "Cryo and Stasis": "Analyze the cryo pipe until it reads below 170 K, then load the cryoxadone beaker into the pod.",
    "Paramedic Bay and Morgue": "Unfold a roller bed and a body bag, then find yourself on the crew monitoring console.",
    "Chief Medical Officer's Office": "Check crew monitoring and the medical records console.",
    "Cargo Bay (Cargo Technician)": "Order something cheap, approve it, and collect it from the telepad.",
    "Quartermaster's Office": "Read the bounty list and change a department budget.",
    "Salvage Prep (Salvage Specialist)": "Use the salvage magnet and read its offers.",
    "Mining Field": "Mine ten ore and turn it into sheets at the ore processor in Salvage Prep.",
    "Kitchen (Chef)": "Make dough from 45u flour and 15u water, then microwave it into bread.",
    "Bar (Bartender)": "Mix a Gin and Tonic and examine the glass.",
    "Hydroponics (Botanist)": "Plant the wheat seeds, raise them and harvest wheat.",
    "Service Counter (Service Worker)": "Serve a coffee, then visit the Kitchen, Bar and Hydroponics.",
    "Janitor's Closet (Janitor)": "Fill the mop bucket and take the janitor cart next door.",
    "Messy Room (Janitor practice)": "Mop every puddle and bag every piece of rubbish.",
    "Chapel (Chaplain)": "Hold a short service: take the bible to the altar and say a few words in chat.",
    "Library (Librarian)": "Write a short guide in a blank book and shelve it.",
    "Theater (Clown and Mime)": "Honk the horn (clown) or build an invisible wall (mime).",
    "Music Room (Musician)": "Play a tune on every instrument here.",
    "Crew Lounge (Passenger)": "Get a drink from the vending machine and change clothes at the ClothesMate.",
    "Psychologist's Office": "Read your lessons and practise a short counselling script in chat.",
    "Newsroom (Reporter)": "Publish an article about your first day on the Tutorial Station.",
    "Arcade": "Beat the arcade machine.",
}

TASKS.update(ANTAG_TASKS)
TASKS.update(AWARENESS_TASKS)
TASKS["Operating Theatre and Implants"] = "Implant the monkey with the tracking implant, then examine the empty implanter."
MEDICAL.append(OPERATING_THEATRE)
