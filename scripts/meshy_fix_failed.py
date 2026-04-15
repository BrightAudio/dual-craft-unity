#!/usr/bin/env python3
"""Fix the 5 failed assets with valid aspect ratios."""
import urllib.request, json, ssl, time, os

API_BASE = "https://api.meshy.ai/openapi/v1"
API_KEY = os.environ.get("MESHY_API_KEY", "")
if not API_KEY:
    print("Set MESHY_API_KEY"); exit(1)

PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CARD_DIR = os.path.join(PROJECT, "Assets", "Resources", "CardArt")
UI_DIR = os.path.join(PROJECT, "Assets", "Resources", "UI")

PKM = "vibrant colorful Pokemon TCG card art style, cel-shaded, clean bold lines, bright saturated colors, anime-inspired, Ken Sugimori illustration style, dynamic pose, high detail"


def submit_and_wait(name, prompt, aspect, out_dir):
    headers = {"Authorization": f"Bearer {API_KEY}", "Content-Type": "application/json"}
    data = json.dumps({"ai_model": "nano-banana", "prompt": prompt, "aspect_ratio": aspect}).encode()
    ctx = ssl.create_default_context()

    req = urllib.request.Request(f"{API_BASE}/text-to-image", data=data, headers=headers, method="POST")
    resp = urllib.request.urlopen(req, context=ctx, timeout=30)
    task_id = json.loads(resp.read().decode())["result"]
    print(f"Submitted {name} -> {task_id[:12]}...")

    for _ in range(60):
        time.sleep(5)
        req2 = urllib.request.Request(f"{API_BASE}/text-to-image/{task_id}", headers=headers)
        resp2 = urllib.request.urlopen(req2, context=ctx, timeout=30)
        result = json.loads(resp2.read().decode())
        status = result.get("status")
        if status == "SUCCEEDED":
            url = result["image_urls"][0]
            img_req = urllib.request.Request(url)
            img_data = urllib.request.urlopen(img_req, context=ctx, timeout=60).read()
            path = os.path.join(out_dir, f"{name}.png")
            os.makedirs(out_dir, exist_ok=True)
            with open(path, "wb") as f:
                f.write(img_data)
            print(f"  DONE: {name}.png ({len(img_data)//1024}KB)")
            return True
        elif status == "FAILED":
            print(f"  FAILED: {name}")
            return False
        print(f"  {name}: {status} ({result.get('progress', 0)}%)")
    return False


tasks = [
    ("d-air-7", f"Cyclone Djinn, a powerful tornado spirit with muscular wind form, spinning vortex body, air elemental, {PKM}", "3:4", CARD_DIR),
    ("btn-gold", f"A bright golden game button with embossed text area, shiny metallic, Pokemon game UI button, wide rectangle, {PKM}", "4:3", UI_DIR),
    ("btn-red", f"A bright red game button with white border, glossy, Pokemon game UI button, wide rectangle, {PKM}", "4:3", UI_DIR),
    ("divider-gold", f"A thin horizontal golden ornamental divider line with small star decorations, game UI separator element, {PKM}", "16:9", UI_DIR),
    ("deck-panel-bg", f"A warm wooden panel background for deck display, card game table surface, {PKM}", "16:9", UI_DIR),
]

for name, prompt, ar, out_dir in tasks:
    try:
        submit_and_wait(name, prompt, ar, out_dir)
    except Exception as e:
        print(f"Error with {name}: {e}")
    time.sleep(1)

print("\nAll retry tasks complete!")
