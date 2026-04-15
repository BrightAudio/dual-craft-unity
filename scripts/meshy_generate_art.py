#!/usr/bin/env python3
"""
Dual Craft — Meshy AI Card Art Generator
=========================================
Generates high-quality TCG card art using Meshy AI's Text-to-Image API.
Outputs 512x512 portrait PNGs to Assets/Resources/CardArt/.

Usage:
    export MESHY_API_KEY="your-api-key-here"
    python3 scripts/meshy_generate_art.py                 # Generate all missing card art
    python3 scripts/meshy_generate_art.py --card flame_drake  # Generate one specific card
    python3 scripts/meshy_generate_art.py --list          # List cards needing art
    python3 scripts/meshy_generate_art.py --status        # Check pending task status

Sign up at https://www.meshy.ai/ for an API key (free tier available).
"""

import os, sys, json, time, urllib.request, urllib.error, ssl

PROJECT_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CARD_ART_DIR = os.path.join(PROJECT_ROOT, "Assets", "Resources", "CardArt")
TASK_CACHE = os.path.join(PROJECT_ROOT, "scripts", ".meshy_tasks.json")
API_BASE = "https://api.meshy.ai/openapi/v1"

# ─── Card Art Prompts ──────────────────────────────────
# Format: card_id → (category, element, creature_type, description)
CARD_PROMPTS = {
    # ═══ DAEMON CARDS — creature art, 3:4 portrait ════════
    # Flame Elementals
    "flame_drake": ("daemon", "flame", "elemental",
        "A fearsome fire drake wreathed in orange flames, scales glowing with inner heat, dark fantasy TCG card art, detailed illustration, dramatic lighting"),
    "ember_golem": ("daemon", "flame", "machine",
        "A towering golem of molten rock and steel, lava flowing through mechanical joints, dark fantasy TCG art, epic scale"),
    "inferno_wraith": ("daemon", "flame", "undead",
        "A skeletal wraith engulfed in ghostly blue and orange flames, hollow burning eyes, dark fantasy TCG card art"),
    "blaze_automaton": ("daemon", "flame", "artificial",
        "An elegant mechanical phoenix construct with brass and copper plating, flame core visible through glass chest, steampunk fantasy TCG art"),
    "magma_spirit": ("daemon", "flame", "spirit",
        "An ethereal fire spirit made of swirling plasma and embers, translucent body with bright core, mystical TCG card art"),

    # Ice Elementals
    "frost_titan": ("daemon", "ice", "elemental",
        "A massive ice elemental titan with crystalline armor, freezing mist swirling around it, glacial blue glow, dark fantasy TCG art"),
    "cryo_sentinel": ("daemon", "ice", "machine",
        "A robotic sentinel covered in frost, ice crystal weapons, cold blue LED eyes, sci-fi fantasy TCG card art"),
    "frozen_revenant": ("daemon", "ice", "undead",
        "An undead knight frozen in ice, spectral blue eyes glowing through frost-covered armor, dark fantasy TCG art"),
    "ice_construct": ("daemon", "ice", "artificial",
        "A geometric ice construct with perfect crystalline geometry, hovering runic ice shards, mystical TCG card art"),
    "winter_phantom": ("daemon", "ice", "spirit",
        "A ghostly winter spirit with flowing translucent robes of snow and ice, aurora borealis emanating from form, ethereal TCG art"),

    # Water
    "tide_serpent": ("daemon", "water", "elemental",
        "A colossal sea serpent made of living water, bioluminescent patterns, ocean waves crashing around it, dark fantasy TCG art"),
    "depth_dredger": ("daemon", "water", "machine",
        "A deep-sea mechanical diving suit with tentacle attachments, pressure gauges, barnacle-covered steel, steampunk TCG art"),
    "drowned_captain": ("daemon", "water", "undead",
        "An undead pirate captain with waterlogged ghostly form, seaweed and barnacles, glowing spectral eyes, dark fantasy TCG art"),

    # Earth
    "stone_guardian": ("daemon", "earth", "elemental",
        "A massive stone golem guardian covered in ancient runes, moss and crystal growths, dark fantasy TCG card art"),
    "tectonic_mech": ("daemon", "earth", "machine",
        "A heavy earth-moving mech with drill arms and reinforced plating, rocks and dirt cascading off, industrial fantasy TCG art"),
    "tomb_stalker": ("daemon", "earth", "undead",
        "A skeletal creature emerging from cracked earth, bone armor with gemstone inlays, underground horror TCG art"),

    # Air
    "storm_hawk": ("daemon", "air", "elemental",
        "A majestic storm hawk made of crackling lightning and wind, feathers of cloud and electricity, dynamic fantasy TCG art"),
    "aero_drone": ("daemon", "air", "artificial",
        "An ornate flying construct with gossamer crystal wings, anti-gravity engines, hovering elegantly, fantasy steampunk TCG art"),

    # Light
    "radiant_seraph": ("daemon", "light", "spirit",
        "A radiant angelic seraph with six golden wings, blinding holy light, divine armor, celestial fantasy TCG card art"),
    "solar_engine": ("daemon", "light", "machine",
        "A sun-powered mechanical guardian with golden plating, concentrated light beams from chest, solarpunk fantasy TCG art"),

    # Dark
    "shadow_fiend": ("daemon", "dark", "spirit",
        "A terrifying shadow fiend with multiple glowing purple eyes, wisps of darkness, clawed ethereal form, dark fantasy TCG art"),
    "void_reaper": ("daemon", "dark", "undead",
        "A skeletal reaper emerging from a void portal, tattered dark robes, scythe of pure darkness, gothic horror TCG art"),
    "nightmare_engine": ("daemon", "dark", "machine",
        "A nightmarish mechanical horror with exposed gears and dark energy conduits, fear-inducing design, dark steampunk TCG art"),

    # Nature
    "ancient_treant": ("daemon", "nature", "elemental",
        "An ancient treant with a face of bark and moss, glowing green eyes, cherry blossoms growing from branches, forest fantasy TCG art"),
    "fungal_zombie": ("daemon", "nature", "undead",
        "A zombie overgrown with bioluminescent fungi and vines, spore clouds, eerie green glow, body horror fantasy TCG art"),
    "growth_golem": ("daemon", "nature", "artificial",
        "A construct made of woven living wood and enchanted vines, flower blossoms as joints, nature magic TCG art"),

    # ═══ PILLAR CARDS — mystical structures ════════════════
    "flame_pillar": ("pillar", "flame", None,
        "A towering pillar of fire and obsidian, ancient runes glowing along its surface, lava pooling at base, fantasy TCG art"),
    "ice_pillar": ("pillar", "ice", None,
        "A crystalline ice pillar radiating cold blue energy, frost patterns spiraling outward, frozen throne room setting, fantasy TCG art"),
    "water_pillar": ("pillar", "water", None,
        "A pillar of living water suspended in mid-air, fish and coral visible within, ocean energy radiating, mystical TCG art"),
    "earth_pillar": ("pillar", "earth", None,
        "A massive stone monolith covered in ancient dwarven runes, crystal veins pulsing with energy, underground cavern, fantasy TCG art"),
    "air_pillar": ("pillar", "air", None,
        "A floating pillar of compressed air and storm clouds, lightning arcing between suspended stone fragments, sky temple, TCG art"),
    "light_pillar": ("pillar", "light", None,
        "A golden pillar of radiant light ascending to heavens, angelic sigils floating around it, divine temple setting, fantasy TCG art"),
    "dark_pillar": ("pillar", "dark", None,
        "A pillar of pure shadow and void energy, dark crystals orbiting, eldritch symbols pulsing with purple light, gothic TCG art"),
    "nature_pillar": ("pillar", "nature", None,
        "A massive ancient tree with branch pillar reaching skyward, roots spreading across ruins, druidic runes glowing green, fantasy TCG art"),

    # ═══ CONJUROR CARDS — powerful mage characters ═════════
    "pyromancer": ("conjuror", "flame", None,
        "Portrait of a powerful pyromancer, flames swirling around hands, ornate red and gold robes, intense eyes, dark fantasy TCG art"),
    "cryomancer": ("conjuror", "ice", None,
        "Portrait of an ice mage with frost crown, blue crystalline staff, cold breath visible, pale skin, winter fantasy TCG art"),
    "hydromancer": ("conjuror", "water", None,
        "Portrait of an aquatic mage with flowing water robes, trident staff, ocean aura, deep blue tones, fantasy TCG card art"),
    "geomancer": ("conjuror", "earth", None,
        "Portrait of a stone mage with crystal-embedded armor, floating rocks around fists, cave setting, earth-toned fantasy TCG art"),
    "aeromancer": ("conjuror", "air", None,
        "Portrait of a wind mage levitating with storm energy, flowing white robes, lightning eyes, sky setting, fantasy TCG art"),
    "lumimancer": ("conjuror", "light", None,
        "Portrait of a holy light mage with golden halo, white and gold vestments, radiant aura, cathedral setting, fantasy TCG art"),
    "umbramancer": ("conjuror", "dark", None,
        "Portrait of a shadow mage with void eyes, dark energy coiling from hands, tattered robes, dark castle, gothic fantasy TCG art"),
    "verdamancer": ("conjuror", "nature", None,
        "Portrait of a druid mage with antler crown, vine-wrapped staff, forest spirit companions, green aura, fantasy TCG art"),
}


def get_api_key():
    key = os.environ.get("MESHY_API_KEY", "").strip()
    if not key:
        print("ERROR: Set MESHY_API_KEY environment variable")
        print("  export MESHY_API_KEY='your-key-here'")
        print("  Get one at: https://www.meshy.ai/")
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
        print(f"  API Error {e.code}: {err_body}")
        return None


def load_task_cache():
    if os.path.exists(TASK_CACHE):
        with open(TASK_CACHE) as f:
            return json.load(f)
    return {}


def save_task_cache(cache):
    with open(TASK_CACHE, "w") as f:
        json.dump(cache, f, indent=2)


def create_task(card_id, prompt, api_key):
    """Submit a text-to-image task to Meshy."""
    data = {
        "ai_model": "nano-banana",
        "prompt": prompt,
        "aspect_ratio": "3:4",  # Portrait for card art
    }
    result = api_request("POST", "/text-to-image", data, api_key)
    if result and "result" in result:
        task_id = result["result"]
        print(f"  Created task {task_id} for {card_id}")
        return task_id
    return None


def check_task(task_id, api_key):
    """Check status of a task."""
    return api_request("GET", f"/text-to-image/{task_id}", api_key=api_key)


def download_image(url, output_path):
    """Download an image from URL."""
    ctx = ssl.create_default_context()
    req = urllib.request.Request(url)
    with urllib.request.urlopen(req, context=ctx) as resp:
        data = resp.read()
    with open(output_path, "wb") as f:
        f.write(data)
    print(f"  Downloaded → {os.path.basename(output_path)}")


def list_missing():
    """List cards that don't have art yet."""
    os.makedirs(CARD_ART_DIR, exist_ok=True)
    existing = {os.path.splitext(f)[0] for f in os.listdir(CARD_ART_DIR) if f.endswith(".png")}
    missing = []
    for card_id in CARD_PROMPTS:
        if card_id not in existing:
            cat, elem, ctype, _ = CARD_PROMPTS[card_id]
            label = f"{elem} {ctype or ''} {cat}".strip()
            missing.append((card_id, label))
    return missing


def generate_all(api_key):
    """Generate art for all cards missing art."""
    missing = list_missing()
    if not missing:
        print("All cards already have art!")
        return

    cache = load_task_cache()
    print(f"\n{len(missing)} cards need art. Starting generation...\n")

    for card_id, label in missing:
        if card_id in cache and cache[card_id].get("status") != "FAILED":
            print(f"  {card_id}: already submitted (task {cache[card_id]['task_id']})")
            continue

        _, _, _, prompt = CARD_PROMPTS[card_id]
        task_id = create_task(card_id, prompt, api_key)
        if task_id:
            cache[card_id] = {"task_id": task_id, "status": "PENDING"}
            save_task_cache(cache)
            time.sleep(1)  # Rate limit courtesy

    # Now poll for results
    print("\nPolling for results...")
    pending = {cid for cid, info in cache.items() if info.get("status") in ("PENDING", "IN_PROGRESS")}
    max_polls = 60  # ~5 minutes max wait

    for poll in range(max_polls):
        if not pending:
            break
        time.sleep(5)
        for card_id in list(pending):
            task_id = cache[card_id]["task_id"]
            result = check_task(task_id, api_key)
            if not result:
                continue

            status = result.get("status", "UNKNOWN")
            cache[card_id]["status"] = status

            if status == "SUCCEEDED":
                urls = result.get("image_urls", [])
                if urls:
                    out_path = os.path.join(CARD_ART_DIR, f"{card_id}.png")
                    download_image(urls[0], out_path)
                pending.discard(card_id)
            elif status == "FAILED":
                err = result.get("task_error", {}).get("message", "Unknown error")
                print(f"  FAILED: {card_id} — {err}")
                pending.discard(card_id)
            else:
                progress = result.get("progress", 0)
                print(f"  {card_id}: {status} ({progress}%)")

        save_task_cache(cache)

    if pending:
        print(f"\n{len(pending)} tasks still pending. Run --status to check later.")


def generate_one(card_id, api_key):
    """Generate art for a single card."""
    if card_id not in CARD_PROMPTS:
        print(f"Unknown card: {card_id}")
        print(f"Available: {', '.join(sorted(CARD_PROMPTS.keys()))}")
        return

    _, _, _, prompt = CARD_PROMPTS[card_id]
    task_id = create_task(card_id, prompt, api_key)
    if not task_id:
        return

    cache = load_task_cache()
    cache[card_id] = {"task_id": task_id, "status": "PENDING"}
    save_task_cache(cache)

    print("Waiting for result...")
    for _ in range(60):
        time.sleep(5)
        result = check_task(task_id, api_key)
        if not result:
            continue
        status = result.get("status")
        if status == "SUCCEEDED":
            urls = result.get("image_urls", [])
            if urls:
                out_path = os.path.join(CARD_ART_DIR, f"{card_id}.png")
                download_image(urls[0], out_path)
                cache[card_id]["status"] = "SUCCEEDED"
                save_task_cache(cache)
            return
        elif status == "FAILED":
            print(f"Failed: {result.get('task_error', {}).get('message', '?')}")
            return
        print(f"  {status} ({result.get('progress', 0)}%)")


def check_status(api_key):
    """Check status of all pending tasks."""
    cache = load_task_cache()
    if not cache:
        print("No tasks submitted yet.")
        return

    for card_id, info in sorted(cache.items()):
        if info["status"] in ("SUCCEEDED", "FAILED"):
            print(f"  {card_id}: {info['status']}")
            continue
        result = check_task(info["task_id"], api_key)
        if result:
            status = result.get("status", "UNKNOWN")
            cache[card_id]["status"] = status
            if status == "SUCCEEDED":
                urls = result.get("image_urls", [])
                if urls:
                    out_path = os.path.join(CARD_ART_DIR, f"{card_id}.png")
                    download_image(urls[0], out_path)
            print(f"  {card_id}: {status} ({result.get('progress', 0)}%)")
    save_task_cache(cache)


if __name__ == "__main__":
    if "--list" in sys.argv:
        missing = list_missing()
        if missing:
            print(f"{len(missing)} cards need art:")
            for cid, label in missing:
                print(f"  {cid} ({label})")
        else:
            print("All cards have art!")
        sys.exit(0)

    api_key = get_api_key()

    if "--status" in sys.argv:
        check_status(api_key)
    elif "--card" in sys.argv:
        idx = sys.argv.index("--card")
        if idx + 1 < len(sys.argv):
            generate_one(sys.argv[idx + 1], api_key)
        else:
            print("Usage: --card <card_id>")
    else:
        generate_all(api_key)
