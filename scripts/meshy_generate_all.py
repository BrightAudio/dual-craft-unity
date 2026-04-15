#!/usr/bin/env python3
"""
Dual Craft — Comprehensive Meshy AI Art Generator
===================================================
Generates ALL game assets via Meshy Text-to-Image API:
  - Card art (daemons, pillars, conjurors, spells, masks, seals, dispels)
  - Battle board backgrounds and zones
  - UI elements (frames, icons, buttons, panels)
  - Scene backgrounds (menu, collection, deck builder, pack opening)

Usage:
    export MESHY_API_KEY="your-key"
    python3 scripts/meshy_generate_all.py              # Generate everything
    python3 scripts/meshy_generate_all.py --category ui  # Only UI assets  
    python3 scripts/meshy_generate_all.py --status     # Check pending
    python3 scripts/meshy_generate_all.py --download   # Download completed
"""

import os, sys, json, time, urllib.request, urllib.error, ssl

PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CARD_ART_DIR = os.path.join(PROJECT, "Assets", "Resources", "CardArt")
UI_DIR = os.path.join(PROJECT, "Assets", "Resources", "UI")
CACHE_FILE = os.path.join(PROJECT, "scripts", ".meshy_all_tasks.json")
API_BASE = "https://api.meshy.ai/openapi/v1"

# ════════════════════════════════════════════════════════════
#  CARD ART PROMPTS — keyed by existing filenames
# ════════════════════════════════════════════════════════════
# These match the actual files in Assets/Resources/CardArt/
STYLE_SUFFIX = ", high detail digital painting, TCG card art, dramatic lighting, dark fantasy"

CARD_ART = {}

# ─── Daemon cards (d-{element}-{n}) ───
DAEMON_PROMPTS = {
    "flame": [
        "A ferocious fire drake wreathed in roaring orange flames, obsidian scales glowing with inner molten heat",
        "A towering inferno golem of molten rock and liquid steel, lava flowing through mechanical joints",
        "A spectral flame wraith with hollow burning eye sockets, ghostly blue and orange fire billowing from skeletal form",
        "An elegant brass phoenix automaton with crystalline flame core visible through ornate glass chest plate",
        "A swirling plasma spirit made of pure living fire, embers trailing from translucent ethereal body",
        "A demonic fire elemental lord with crown of eternal flames, magma armor plates, volcanic throne",
        "A cunning flame fox spirit with nine burning tails, mystic fire runes floating around it",
    ],
    "ice": [
        "A massive ice titan with crystalline glacier armor, freezing mist swirling, glacial blue glow emanating from core",
        "A frost-covered robotic sentinel with ice crystal blades, cold blue LED eyes scanning through blizzard",
        "An undead frost knight frozen solid in translucent ice armor, spectral blue eyes glowing within",
        "A perfect geometric ice construct of hovering crystalline shards, runic frost sigils orbiting",
        "A ghostly winter spirit with flowing translucent robes of snow and starlight, aurora borealis halo",
        "An ancient ice dragon coiled around a frozen spire, breath of absolute zero, diamond-hard scales",
        "A frost fairy queen on a throne of black ice, delicate ice-crystal wings, commanding blizzard",
    ],
    "water": [
        "A colossal sea serpent made of living ocean water, bioluminescent patterns rippling along body",
        "A deep-sea mechanical diving suit with kraken tentacle attachments, barnacle-encrusted brass hull",
        "An undead pirate captain with waterlogged ghostly translucent form, spectral ship in background",
        "A water elemental shaped like a towering wave with a face, coral crown, ocean depths below",
        "A beautiful water nymph spirit with flowing liquid hair, holding pearl scepter, underwater palace",
        "A massive hermit crab with a fortress shell, cannons protruding, crashing through waves",
        "A jellyfish phantom glowing with ethereal deep-sea light, trailing translucent tentacles of energy",
    ],
    "earth": [
        "A massive stone golem guardian covered in ancient glowing runes, moss and crystal growths along body",
        "A heavy mechanical earth-mover with diamond drill arms, rocks and boulders cascading off",
        "A skeletal creature emerging from cracked earth tomb, bone armor inlaid with raw gemstones",
        "A towering mountain elemental of layered sedimentary rock, crystals sprouting from shoulders",
        "A subterranean wyrm made of stone and precious metals, tunneling through cavern walls",
        "A dwarven earth spirit guardian with granite beard, holding massive stone warhammer, rune-covered",
        "A living quicksand horror with multiple grabbing hands forming from desert earth",
    ],
    "air": [
        "A majestic storm hawk made of crackling lightning and wind, feathers of cloud and electricity",
        "An ornate flying construct with gossamer crystal wings, anti-gravity rune engines glowing",
        "A tornado elemental with a face in the eye of the storm, debris spiraling around body",
        "A spectral wind dancer spirit made of swirling aurora lights and gentle breezes",
        "A thunderbird of pure electrical energy, massive wingspan crackling with chain lightning",
        "A cloud giant warrior standing atop a storm front, lightning bolt javelin in hand",
        "A sylph air spirit with translucent dragonfly wings, leaving trails of sparkling wind",
    ],
    "light": [
        "A radiant angelic seraph with six golden wings spread wide, blinding holy light, divine armor",
        "A sun-powered mechanical guardian with polished golden plating, concentrated light beams from chest",
        "A holy paladin spirit in gleaming white and gold plate armor, radiant sword raised high",
        "A living prism creature refracting rainbow light in all directions, crystalline geometric body",
        "A phoenix of pure golden light, rebirth flames of white and gold, celestial temple backdrop",
        "A celestial clockwork angel with gear-wing mechanisms, shooting concentrated light arrows",
        "A star-born elemental of compressed solar plasma, corona of light, cosmic background",
    ],
    "dark": [
        "A terrifying shadow fiend with multiple glowing purple eyes, wisps of darkness, ethereal claws",
        "A skeletal grim reaper emerging from void portal, tattered dark robes, scythe of pure darkness",
        "A nightmarish mechanical horror with exposed gears and dark energy conduits, fear-inducing",
        "A vampire lord in elaborate gothic armor, blood-red eyes, bat-like shadow wings unfurling",
        "A living shadow that consumes light, formless except for a single burning red eye",
        "An eldritch abomination of writhing shadow tentacles, alien geometry, maddening to behold",
        "A dark mirror spirit reflecting a corrupted twisted version of the viewer, cracked glass frame",
    ],
    "nature": [
        "An ancient treant with face of bark and moss, glowing green eyes, cherry blossoms from branches",
        "A zombie overgrown with bioluminescent fungi and vines, spore clouds, eerie green glow",
        "A construct of woven living wood and enchanted vines, flower blossoms blooming as joints",
        "A massive carnivorous plant monster with snapping Venus flytrap heads, jungle setting",
        "A forest stag spirit with antlers of living wood, fireflies surrounding, ancient forest",
        "A swamp creature made of tangled roots and lily pads, glowing mushrooms growing on body",
        "A tiny but fierce thorn sprite warrior riding a giant beetle, wielding rose-thorn lance",
    ],
}

for element, prompts in DAEMON_PROMPTS.items():
    for i, prompt in enumerate(prompts, 1):
        CARD_ART[f"d-{element}-{i}"] = ("CardArt", "3:4", prompt + STYLE_SUFFIX)

# Special daemons
CARD_ART["d-fenrir"] = ("CardArt", "3:4", "Fenrir the legendary giant wolf of Norse mythology, massive jaws open, chains breaking, cosmic ice and fire background" + STYLE_SUFFIX)
CARD_ART["d-fox-fledgling"] = ("CardArt", "3:4", "A cute baby fox spirit daemon with small flame-tipped tail, big curious eyes, mystical forest clearing" + STYLE_SUFFIX)
CARD_ART["d-fox-grey"] = ("CardArt", "3:4", "A wise grey fox spirit with silver ethereal fur, multiple ghostly tails, moonlit mountain shrine" + STYLE_SUFFIX)

# ─── Conjuror cards (cj-{element}) ───
CONJUROR_PROMPTS = {
    "flame": "Portrait of a powerful pyromancer, flames swirling from hands, ornate red and gold battle robes, fierce eyes",
    "ice": "Portrait of an ice mage with frost crown, blue crystalline staff, cold breath visible, pale glowing skin",
    "water": "Portrait of an aquatic sorcerer with flowing water robes, coral-encrusted trident staff, deep blue aura",
    "earth": "Portrait of a stone mage with crystal-embedded armor, floating rocks orbiting fists, underground cavern",
    "air": "Portrait of a wind mage levitating amid storm clouds, flowing white robes, eyes of pure lightning",
    "light": "Portrait of a holy mage with golden halo and divine vestments, radiant healing aura, cathedral backdrop",
    "dark": "Portrait of a shadow mage with void-black eyes, dark energy coiling around hands, gothic castle ruins",
    "nature": "Portrait of a druid with antler crown, vine-wrapped living-wood staff, forest spirit companions hovering",
}
for elem, prompt in CONJUROR_PROMPTS.items():
    CARD_ART[f"cj-{elem}"] = ("CardArt", "3:4", prompt + STYLE_SUFFIX)

# ─── Pillar cards (p-{type}-{n}) ───
PILLAR_PROMPTS = {
    "flame": ["A towering pillar of fire and obsidian with ancient runes glowing, lava pooling at base",
              "An erupting volcanic pillar channeling streams of magma skyward, smoke and ember clouds"],
    "ice": ["A crystalline ice pillar radiating cold blue energy, frost patterns spiraling outward",
            "A frozen monolith of black ice with trapped spectral forms visible within, aurora overhead"],
    "water": ["A pillar of living suspended water with fish and coral visible, ocean energy radiating",
              "A deep whirlpool pillar descending into an abyss, bioluminescent sea creatures circling"],
    "earth": ["A massive stone monolith with ancient rune veins pulsing with amber energy, cave setting",
              "A pillar of raw gemstones and crystals growing from deep earth, geode interior visible"],
    "air": ["A floating pillar of compressed stormy air, lightning arcing between suspended stone shards",
            "A tornado pillar reaching from ground to sky, feathers and leaves spiraling within"],
    "light": ["A golden pillar of radiant divine light ascending to heavens, angelic sigils orbiting",
              "A crystal pillar catching and amplifying sunlight into prismatic rainbows, sacred grove"],
    "dark": ["A pillar of pure void shadow, dark crystals orbiting, eldritch symbols pulsing purple",
             "A corrupted obsidian obelisk leaking shadow mist, chains of darkness anchoring to ground"],
    "nature": ["An ancient tree pillar reaching skyward, living roots spreading across stone ruins, druidic runes",
               "A pillar of overgrown vines and flowers around an ancient stone core, jungle temple"],
    "elemental": ["A pillar of swirling combined elemental energies — fire, water, earth, air spiraling together",
                  "A primal elemental nexus pillar crackling with raw natural power, all elements converging"],
    "spirit": ["An ethereal spirit pillar of ghostly translucent energy, souls spiraling upward, moonlit",
               "A spectral pillar of concentrated spirit energy pulsing between dimensions, shimmering"],
    "undead": ["A pillar of bleached bone and necrotic energy, skulls embedded, green death aura",
               "A grave pillar of tombstones fused together, spectral chains, necromantic runes glowing"],
    "machine": ["A towering mechanical pillar of gears and pistons, steam venting, clockwork energy core",
                "An industrial power pylon with arcane tech, electricity arcing between conductors"],
    "artificial": ["A sleek synthetic pillar of polished alien alloy, holographic runes, cyan energy lines",
                   "A crystalline AI core pillar with circuits of light, data streams visible, futuristic"],
}
for ptype, prompts in PILLAR_PROMPTS.items():
    for i, prompt in enumerate(prompts, 1):
        CARD_ART[f"p-{ptype}-{i}"] = ("CardArt", "3:4", prompt + STYLE_SUFFIX)

# ─── Spell cards: Seals (s-{n}), Dispels (disp-{n}), Masks (m-{n}), Fields (f-{n}) ───
SEAL_PROMPTS = [
    "A glowing magical seal circle on stone floor, intricate arcane geometry, binding chains of light",
    "A ward seal of ice and fire intertwined, protective barrier dome visible, ancient script",
    "A nature seal of vines and thorns forming a pentagram, druidic containment magic",
    "A dark seal of shadow bindings, chains of darkness wrapped around a glowing prison sphere",
    "A celestial seal with orbiting star fragments, divine containment circle, golden glow",
]
for i, prompt in enumerate(SEAL_PROMPTS, 1):
    CARD_ART[f"s-{i}"] = ("CardArt", "3:4", prompt + STYLE_SUFFIX)

DISPEL_PROMPTS = [
    "A magical dispel burst — shattered barrier fragments flying outward, arcane energy explosion",
    "A wave of purifying white light breaking through dark enchantments, chains shattering",
    "A counter-spell vortex absorbing and nullifying magical energy, swirling arcane disruption",
    "An anti-magic pulse from a rune stone, dissolving nearby enchantments into sparks",
    "A temporal dispel rewinding magic, clock gears and broken spells frozen mid-dissolution",
    "A nature dispel — thorny vines crushing and absorbing magical constructs, green absorption",
    "A void dispel tearing a hole in reality to swallow enemy magic, dark rift consuming spells",
]
for i, prompt in enumerate(DISPEL_PROMPTS, 1):
    CARD_ART[f"disp-{i}"] = ("CardArt", "3:4", prompt + STYLE_SUFFIX)

MASK_PROMPTS = [
    "An ornate golden mask floating in darkness, mysterious eyes glowing through slits, arcane power",
    "A cracked porcelain mask with shadow leaking from fractures, one eye visible, haunting",
    "A nature mask of leaves and bark with glowing green eye sockets, forest spirit embodied",
    "A mechanical mask of gears and lenses, steam venting, analysis mode engaged, brass finish",
    "A frozen ice mask with crystalline features, cold mist emanating, winter's visage",
]
for i, prompt in enumerate(MASK_PROMPTS, 1):
    CARD_ART[f"m-{i}"] = ("CardArt", "3:4", prompt + STYLE_SUFFIX)

FIELD_PROMPTS = [
    "A magical battlefield domain — volcanic hellscape with lava rivers, obsidian pillars, red sky",
    "A frozen tundra domain — endless ice plains, blizzard, aurora borealis, crystal formations",
    "A deep ocean domain — underwater coral palace, bioluminescent fish, sunlight filtering down",
    "An enchanted forest domain — massive ancient trees, glowing mushrooms, fairy lights, mystical fog",
    "A celestial domain — floating platforms in starry void, golden bridges of light, cosmic energy",
    "A shadow domain — twisted dark landscape, dead trees, purple lightning, ominous castle silhouette",
    "A mechanical domain — vast clockwork factory, gears turning, steam pipes, bronze and iron cityscape",
]
for i, prompt in enumerate(FIELD_PROMPTS, 1):
    CARD_ART[f"f-{i}"] = ("CardArt", "3:4", prompt + STYLE_SUFFIX)

# Card back
CARD_ART["card-back"] = ("CardArt", "3:4", "Ornate TCG card back design, dark leather texture with intricate gold filigree border, central mystical sigil of dual elements, embossed metallic finish, premium trading card game design" + STYLE_SUFFIX)

# ════════════════════════════════════════════════════════════
#  UI ASSET PROMPTS — replace procedural PNGs
# ════════════════════════════════════════════════════════════
UI_STYLE = ", digital game UI art, clean edges, dark fantasy theme, gold accents on dark background"

UI_ASSETS = {
    # Backgrounds
    "battle-bg": ("UI", "16:9", "Epic dark fantasy battle arena, two opposing platforms over a mystical abyss, arcane energy swirling, dramatic purple and gold lighting, TCG game board background" + UI_STYLE),
    "menu-bg": ("UI", "16:9", "Dark luxurious main menu background for a card game, gothic castle interior with candles and mystical artifacts, warm gold ambient light, ornate stone walls" + UI_STYLE),
    "menu-gradient": ("UI", "16:9", "Dark atmospheric gradient background with subtle magical particles, deep black fading to dark purple, floating ember sparks, ambient card game menu" + UI_STYLE),
    "collection-bg": ("UI", "16:9", "A grand magical library with towering bookshelves and display cases, floating card collections, warm golden candlelight, mystical card game collection room" + UI_STYLE),
    "deckbuilder-bg": ("UI", "16:9", "A mystical crafting workshop table with cards spread out, enchanting tools, crystal lamp, dark wood surface, card game deck building scene" + UI_STYLE),
    "ceremony-bg": ("UI", "16:9", "A grand ceremonial arena with spectators, golden banners, dramatic spotlight on center stage, card game victory ceremony" + UI_STYLE),
    "packopen-bg": ("UI", "16:9", "A mystical altar with glowing card pack on pedestal, magical energy swirling around it, anticipation lighting, card game pack opening" + UI_STYLE),
    "packopen-gradient": ("UI", "16:9", "Dramatic radial burst of golden magical energy on dark background, light rays emanating from center, card reveal moment" + UI_STYLE),
    "pack-showcase-bg": ("UI", "16:9", "An elegant display shelf with three ornate card packs on velvet pedestals, spotlight beams, magical shop atmosphere" + UI_STYLE),

    # Card frames
    "frame-daemon": ("UI", "3:4", "Ornate dark fantasy card frame border for daemon creature cards, sharp metallic edges with red and bronze accents, empty center transparent, intricate corner filigree" + UI_STYLE),
    "frame-pillar": ("UI", "3:4", "Mystical stone pillar card frame border, sturdy granite with blue crystal inlays, empty center transparent, ancient runic carvings on edges" + UI_STYLE),
    "frame-conjuror": ("UI", "3:4", "Elegant mage card frame border, golden ornate scrollwork with purple gem accents, empty center transparent, wizard hat silhouette at top" + UI_STYLE),
    "frame-spell": ("UI", "3:4", "Arcane spell card frame border, swirling magical energy trails in teal and gold, empty center transparent, mystic sigil corners" + UI_STYLE),

    # Card back premium
    "card-back-premium": ("UI", "3:4", "Premium TCG card back design, black leather with elaborate gold dragon filigree, central glowing dual-element crystal sigil, metallic embossed edges, luxury card game back" + UI_STYLE),

    # Icons (square)
    "icon-heart": ("UI", "1:1", "A glowing red crystalline heart icon with golden outline, single clean game UI icon, dark background, health point symbol" + UI_STYLE),
    "icon-mana": ("UI", "1:1", "A brilliant blue mana crystal icon with golden outline, single clean game UI icon, dark background, magic energy symbol" + UI_STYLE),
    "icon-shield": ("UI", "1:1", "A sturdy golden shield icon with defensive runes, single clean game UI icon, dark background, defense symbol" + UI_STYLE),
    "icon-skull": ("UI", "1:1", "A menacing skull icon with glowing eye sockets, golden outline, single clean game UI icon, dark background, death symbol" + UI_STYLE),
    "icon-sword": ("UI", "1:1", "A gleaming crossed swords icon with golden hilts, single clean game UI icon, dark background, attack power symbol" + UI_STYLE),

    # Buttons
    "btn-gold": ("UI", "16:9", "Wide golden ornate button with embossed edge, metallic sheen, dark center area for text, game UI button, premium feel" + UI_STYLE),
    "btn-red": ("UI", "16:9", "Wide red danger button with dark metallic edge, glowing crimson center, game UI button for destructive actions" + UI_STYLE),

    # Panels and bars
    "panel-dark": ("UI", "16:9", "Dark translucent game UI panel with subtle gold border, slightly transparent center, rounded gold corner accents" + UI_STYLE),
    "panel-header": ("UI", "16:9", "Dark game UI header panel with ornate gold trim at bottom edge, gradient from dark to slightly lighter" + UI_STYLE),
    "header-dark": ("UI", "16:9", "Slim dark game UI header bar with gold accent line, clean minimal design" + UI_STYLE),
    "nav-bar-bg": ("UI", "16:9", "Game navigation bar background, dark metal texture with subtle gold rivets along edges" + UI_STYLE),
    "bar-player": ("UI", "16:9", "Player health bar frame with blue crystal inlay, gold ornate ends, dark background, horizontal game UI" + UI_STYLE),
    "bar-opponent": ("UI", "16:9", "Opponent health bar frame with red crystal inlay, silver-black ornate ends, horizontal game UI" + UI_STYLE),
    "divider-gold": ("UI", "16:9", "Elegant gold horizontal divider line with small ornamental center flourish, thin decorative game UI element" + UI_STYLE),
    "config-bar-bg": ("UI", "16:9", "Slim dark configuration bar with subtle grid pattern, minimal game UI element" + UI_STYLE),
    "filter-bar-bg": ("UI", "16:9", "Dark filter/search bar background with subtle inset border, clean game UI element" + UI_STYLE),
    "search-input-bg": ("UI", "16:9", "Dark search input field with subtle glowing border, minimal game UI text field" + UI_STYLE),
    "detail-panel-bg": ("UI", "3:4", "Dark detail panel for card inspection, ornate gold border, semi-transparent dark interior, game UI" + UI_STYLE),
    "deck-panel-bg": ("UI", "3:4", "Dark deck list panel with gold trim, vertical layout, card slots visible, game UI panel" + UI_STYLE),
    "pool-panel-bg": ("UI", "3:4", "Dark card pool panel with subtle grid overlay, gold accent border, game UI" + UI_STYLE),

    # Battle zones
    "zone-daemon": ("UI", "16:9", "Battle zone for daemon creature placement, dark stone platform with glowing red runes, slight elevation, game UI zone indicator" + UI_STYLE),
    "zone-pillar": ("UI", "16:9", "Battle zone for pillar placement, ancient stone pedestal with blue crystal veins, mystical aura, game UI zone" + UI_STYLE),
    "zone-hand": ("UI", "16:9", "Hand card zone, dark velvet card holder mat with gold edge stitching, slight fan layout guide marks, game UI" + UI_STYLE),

    # Feature images
    "feature-battle": ("UI", "16:9", "Dynamic battle scene between two magical creatures clashing, energy explosion at impact point, TCG battle feature promo" + UI_STYLE),
    "feature-decks": ("UI", "16:9", "Stack of beautifully crafted card decks with ornate boxes, one deck fanned open showing cards, deck building promo" + UI_STYLE),
    "feature-grimoire": ("UI", "16:9", "Ancient magical grimoire opened with glowing pages, cards floating out of book, collection feature promo" + UI_STYLE),
    "feature-shop": ("UI", "16:9", "Mystical card shop interior with display cases of glowing card packs, merchant NPC, shop feature promo" + UI_STYLE),
    "feature-story": ("UI", "16:9", "Epic landscape with a lone traveler approaching a dark castle, story/campaign mode promo art" + UI_STYLE),

    # Card packs
    "pack-inferno": ("UI", "3:4", "A fiery card pack with flames licking the edges, obsidian and red metallic wrapper, dragon emblem, inferno collection" + UI_STYLE),
    "pack-shadow": ("UI", "3:4", "A dark shadowy card pack with void energy wisps, black and purple metallic wrapper, skull emblem, shadow collection" + UI_STYLE),
    "pack-verdant": ("UI", "3:4", "A nature card pack overgrown with vines and flowers, green and gold wrapper, leaf emblem, verdant collection" + UI_STYLE),
    "pack-glow": ("UI", "3:4", "A radiant glowing card pack with golden light, white and gold wrapper, star emblem, celestial collection" + UI_STYLE),
}


# ════════════════════════════════════════════════════════════
#  API FUNCTIONS
# ════════════════════════════════════════════════════════════

def get_api_key():
    key = os.environ.get("MESHY_API_KEY", "").strip()
    if not key:
        print("ERROR: Set MESHY_API_KEY environment variable")
        sys.exit(1)
    return key


def api_request(method, path, data=None, api_key=None):
    url = f"{API_BASE}{path}"
    headers = {"Authorization": f"Bearer {api_key}", "Content-Type": "application/json"}
    body = json.dumps(data).encode() if data else None
    req = urllib.request.Request(url, data=body, headers=headers, method=method)
    ctx = ssl.create_default_context()
    try:
        with urllib.request.urlopen(req, context=ctx) as resp:
            return json.loads(resp.read().decode())
    except urllib.error.HTTPError as e:
        err_body = e.read().decode() if e.fp else ""
        print(f"  API Error {e.code}: {err_body[:200]}")
        return None


def load_cache():
    if os.path.exists(CACHE_FILE):
        with open(CACHE_FILE) as f:
            return json.load(f)
    return {}


def save_cache(cache):
    with open(CACHE_FILE, "w") as f:
        json.dump(cache, f, indent=2)


def download_image(url, output_path):
    ctx = ssl.create_default_context()
    req = urllib.request.Request(url)
    with urllib.request.urlopen(req, context=ctx) as resp:
        data = resp.read()
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    with open(output_path, "wb") as f:
        f.write(data)
    size_kb = len(data) / 1024
    print(f"  Downloaded ({size_kb:.0f}KB) → {os.path.basename(output_path)}")


def get_output_path(asset_id, folder):
    if folder == "CardArt":
        return os.path.join(CARD_ART_DIR, f"{asset_id}.png")
    else:
        return os.path.join(UI_DIR, f"{asset_id}.png")


def submit_tasks(prompts_dict, api_key, cache, force=False):
    """Submit generation tasks for all assets in prompts_dict."""
    submitted = 0
    skipped = 0
    
    for asset_id, (folder, aspect, prompt) in sorted(prompts_dict.items()):
        output_path = get_output_path(asset_id, folder)
        
        # Skip if we already have a completed task (unless force)
        if not force and asset_id in cache:
            status = cache[asset_id].get("status", "")
            if status == "SUCCEEDED":
                skipped += 1
                continue
            if status in ("PENDING", "IN_PROGRESS"):
                skipped += 1
                continue

        data = {
            "ai_model": "nano-banana",
            "prompt": prompt,
            "aspect_ratio": aspect,
        }
        result = api_request("POST", "/text-to-image", data, api_key)
        if result and "result" in result:
            task_id = result["result"]
            cache[asset_id] = {
                "task_id": task_id,
                "status": "PENDING",
                "folder": folder,
            }
            submitted += 1
            print(f"  [{submitted}] Submitted: {asset_id} → {task_id}")
            save_cache(cache)
            time.sleep(0.5)  # Rate limiting
        else:
            print(f"  FAILED to submit: {asset_id}")
            # If rate limited, wait and retry
            if result is None:
                print("  Rate limit likely hit, waiting 10s...")
                time.sleep(10)
                result = api_request("POST", "/text-to-image", data, api_key)
                if result and "result" in result:
                    task_id = result["result"]
                    cache[asset_id] = {"task_id": task_id, "status": "PENDING", "folder": folder}
                    submitted += 1
                    print(f"  [{submitted}] Retry succeeded: {asset_id} → {task_id}")
                    save_cache(cache)
                    time.sleep(1)

    return submitted, skipped


def poll_and_download(cache, api_key, max_polls=120):
    """Poll for completed tasks and download results."""
    pending = {aid for aid, info in cache.items() 
               if info.get("status") in ("PENDING", "IN_PROGRESS")}
    
    if not pending:
        print("No pending tasks to poll.")
        return
    
    print(f"\nPolling {len(pending)} pending tasks...")
    completed_this_round = 0
    
    for poll_num in range(max_polls):
        if not pending:
            break
        
        time.sleep(5)
        batch_done = 0
        
        for asset_id in list(pending):
            info = cache[asset_id]
            task_id = info["task_id"]
            result = api_request("GET", f"/text-to-image/{task_id}", api_key=api_key)
            
            if not result:
                continue
            
            status = result.get("status", "UNKNOWN")
            cache[asset_id]["status"] = status
            
            if status == "SUCCEEDED":
                urls = result.get("image_urls", [])
                if urls:
                    folder = info.get("folder", "CardArt")
                    out_path = get_output_path(asset_id, folder)
                    try:
                        download_image(urls[0], out_path)
                        completed_this_round += 1
                        batch_done += 1
                    except Exception as e:
                        print(f"  Download error for {asset_id}: {e}")
                pending.discard(asset_id)
                
            elif status == "FAILED":
                err = result.get("task_error", {}).get("message", "Unknown")
                print(f"  FAILED: {asset_id} — {err}")
                pending.discard(asset_id)
            else:
                progress = result.get("progress", 0)
                if poll_num % 6 == 0:  # Print progress every ~30s
                    print(f"  {asset_id}: {status} ({progress}%)")
            
            time.sleep(0.3)  # Don't hammer the API
        
        save_cache(cache)
        
        if batch_done > 0:
            print(f"  Poll {poll_num+1}: +{batch_done} completed ({completed_this_round} total, {len(pending)} remaining)")
    
    if pending:
        print(f"\n{len(pending)} tasks still pending. Run --download to check later.")
    else:
        print(f"\nAll tasks complete! {completed_this_round} images downloaded.")


def check_status(cache, api_key):
    """Print status summary and optionally download completed."""
    counts = {"SUCCEEDED": 0, "FAILED": 0, "PENDING": 0, "IN_PROGRESS": 0, "UNKNOWN": 0}
    
    for asset_id, info in sorted(cache.items()):
        status = info.get("status", "UNKNOWN")
        
        if status in ("PENDING", "IN_PROGRESS"):
            result = api_request("GET", f"/text-to-image/{info['task_id']}", api_key=api_key)
            if result:
                status = result.get("status", status)
                cache[asset_id]["status"] = status
                
                if status == "SUCCEEDED":
                    urls = result.get("image_urls", [])
                    folder = info.get("folder", "CardArt")
                    out_path = get_output_path(asset_id, folder)
                    if urls and not os.path.exists(out_path):
                        try:
                            download_image(urls[0], out_path)
                        except Exception as e:
                            print(f"  Download error: {e}")
            time.sleep(0.3)
        
        counts[status] = counts.get(status, 0) + 1
    
    save_cache(cache)
    print(f"\nStatus Summary:")
    for s, c in sorted(counts.items()):
        if c > 0:
            print(f"  {s}: {c}")
    print(f"  Total tracked: {len(cache)}")


def download_completed(cache, api_key):
    """Download any completed but not-yet-downloaded assets."""
    downloaded = 0
    for asset_id, info in sorted(cache.items()):
        folder = info.get("folder", "CardArt")
        out_path = get_output_path(asset_id, folder)
        
        if os.path.exists(out_path) and os.path.getsize(out_path) > 1000:
            continue  # Already have it
        
        if info.get("status") != "SUCCEEDED":
            # Check if it's done now
            result = api_request("GET", f"/text-to-image/{info['task_id']}", api_key=api_key)
            if result:
                status = result.get("status")
                cache[asset_id]["status"] = status
                if status != "SUCCEEDED":
                    continue
                urls = result.get("image_urls", [])
                if urls:
                    try:
                        download_image(urls[0], out_path)
                        downloaded += 1
                    except Exception as e:
                        print(f"  Error: {e}")
            time.sleep(0.3)
        else:
            # Status says succeeded but file missing — re-fetch URL
            result = api_request("GET", f"/text-to-image/{info['task_id']}", api_key=api_key)
            if result and result.get("status") == "SUCCEEDED":
                urls = result.get("image_urls", [])
                if urls:
                    try:
                        download_image(urls[0], out_path)
                        downloaded += 1
                    except Exception as e:
                        print(f"  Error: {e}")
            time.sleep(0.3)
    
    save_cache(cache)
    print(f"Downloaded {downloaded} images.")


# ════════════════════════════════════════════════════════════
#  MAIN
# ════════════════════════════════════════════════════════════
if __name__ == "__main__":
    args = sys.argv[1:]
    
    # Determine which asset sets to process
    category = None
    if "--category" in args:
        idx = args.index("--category")
        if idx + 1 < len(args):
            category = args[idx + 1].lower()
    
    if category == "cards":
        all_prompts = CARD_ART
    elif category == "ui":
        all_prompts = UI_ASSETS
    else:
        all_prompts = {**CARD_ART, **UI_ASSETS}
    
    print(f"Dual Craft Asset Generator — {len(all_prompts)} assets configured")
    print(f"  Card art: {len(CARD_ART)}, UI assets: {len(UI_ASSETS)}")
    
    if "--list" in args:
        for aid in sorted(all_prompts):
            folder = all_prompts[aid][0]
            out = get_output_path(aid, folder)
            status = "EXISTS" if os.path.exists(out) and os.path.getsize(out) > 1000 else "MISSING"
            print(f"  [{status}] {aid}")
        sys.exit(0)
    
    force = "--force" in args
    api_key = get_api_key()
    cache = load_cache()
    
    if "--status" in args:
        check_status(cache, api_key)
    elif "--download" in args:
        download_completed(cache, api_key)
    else:
        print(f"\nSubmitting generation tasks...{' (FORCE mode — regenerating all)' if force else ''}")
        submitted, skipped = submit_tasks(all_prompts, api_key, cache, force=force)
        print(f"\nSubmitted: {submitted}, Skipped (already tracked): {skipped}")
        
        if submitted > 0 or any(v.get("status") in ("PENDING", "IN_PROGRESS") for v in cache.values()):
            poll_and_download(cache, api_key)
        
        # Final summary
        succeeded = sum(1 for v in cache.values() if v.get("status") == "SUCCEEDED")
        failed = sum(1 for v in cache.values() if v.get("status") == "FAILED")
        pending = sum(1 for v in cache.values() if v.get("status") in ("PENDING", "IN_PROGRESS"))
        print(f"\n{'='*50}")
        print(f"FINAL: {succeeded} succeeded, {failed} failed, {pending} pending")
        print(f"{'='*50}")
