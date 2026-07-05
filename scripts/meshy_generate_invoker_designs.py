#!/usr/bin/env python3
import json
import os
import ssl
import sys
import time
import urllib.request
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parents[1]
UI_DIR = PROJECT_ROOT / "Assets" / "Resources" / "UI"
CACHE_PATH = PROJECT_ROOT / "scripts" / ".meshy_invoker_design_tasks.json"
API_BASE = "https://api.meshy.ai/openapi/v1"

STYLE = (
    "premium fantasy trading card character portrait, DualMon invoker, "
    "magic academy summoner, visible spirit energy aura, clean readable silhouette, "
    "sharp face and costume detail, no text, no logo, no watermark, dramatic painted game art"
)

PROMPTS = {
    "invoker-design-arcane": (
        "young arcane novice invoker with gold and violet robes, glowing grimoire, "
        "balanced confident expression, blue-white spirit energy sigils. " + STYLE
    ),
    "invoker-design-verdant": (
        "verdant binder invoker with emerald cloak, vine-shaped spirit energy, leaf charms, "
        "warm heroic expression, nature magic aura. " + STYLE
    ),
    "invoker-design-storm": (
        "storm scribe invoker with blue-white coat, lightning spirit energy marks, windblown hair, "
        "charged magical book, electric aura. " + STYLE
    ),
    "invoker-design-umbral": (
        "umbral caller invoker with dark cloak, violet source glow, shadow sigils around a grimoire, "
        "mysterious but heroic expression. " + STYLE
    ),
}


def load_cache():
    if CACHE_PATH.exists():
        return json.loads(CACHE_PATH.read_text())
    return {}


def save_cache(cache):
    CACHE_PATH.write_text(json.dumps(cache, indent=2, sort_keys=True))


def api_request(method, path, api_key, payload=None):
    data = None if payload is None else json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(f"{API_BASE}/{path.lstrip('/')}", data=data, method=method)
    req.add_header("Authorization", f"Bearer {api_key}")
    req.add_header("Content-Type", "application/json")
    with urllib.request.urlopen(req, context=ssl.create_default_context(), timeout=180) as resp:
        return json.loads(resp.read().decode("utf-8"))


def download(url, path):
    with urllib.request.urlopen(urllib.request.Request(url), context=ssl.create_default_context(), timeout=180) as resp:
        data = resp.read()
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)
    print(f"downloaded {path} ({len(data) // 1024} KB)")


def main():
    api_key = os.environ.get("MESHY_API_KEY", "").strip()
    if not api_key:
        print("ERROR: MESHY_API_KEY is required", file=sys.stderr)
        return 1

    force = "--force" in sys.argv
    download_only = "--download" in sys.argv
    cache = load_cache()

    if not download_only:
        for asset_id, prompt in PROMPTS.items():
            existing = cache.get(asset_id, {})
            if not force and existing.get("status") in ("PENDING", "IN_PROGRESS", "SUCCEEDED"):
                print(f"{asset_id}: using existing task")
                continue
            result = api_request("POST", "/text-to-image", api_key, {
                "ai_model": "nano-banana",
                "prompt": prompt,
                "aspect_ratio": "1:1",
                "mode": "fast",
            })
            task_id = result.get("result") or result.get("id")
            if not task_id:
                raise RuntimeError(f"Unexpected response for {asset_id}: {result}")
            cache[asset_id] = {"task_id": task_id, "status": "PENDING", "prompt": prompt}
            save_cache(cache)
            print(f"{asset_id}: submitted {task_id}")
            time.sleep(0.75)

    pending = {
        asset_id
        for asset_id in PROMPTS
        if cache.get(asset_id, {}).get("status") in ("PENDING", "IN_PROGRESS", "SUCCEEDED")
    }

    for poll in range(36):
        if not pending:
            break
        if poll > 0:
            time.sleep(5)
        print(f"poll {poll + 1}: {len(pending)} invoker design task(s)")
        for asset_id in sorted(list(pending)):
            info = cache[asset_id]
            result = api_request("GET", f"/text-to-image/{info['task_id']}", api_key)
            status = result.get("status", "UNKNOWN")
            info["status"] = status
            if status == "SUCCEEDED":
                urls = result.get("image_urls") or result.get("output") or []
                if isinstance(urls, str):
                    urls = [urls]
                if urls:
                    download(urls[0], UI_DIR / f"{asset_id}.png")
                else:
                    info["status"] = "FAILED"
                    info["error"] = "No image URLs returned"
                pending.discard(asset_id)
            elif status == "FAILED":
                info["error"] = result.get("task_error") or result.get("error") or "unknown"
                print(f"{asset_id}: failed {info['error']}")
                pending.discard(asset_id)
            else:
                print(f"{asset_id}: {status} {result.get('progress', 0)}%")
            save_cache(cache)
            time.sleep(0.25)

    if pending:
        print(f"{len(pending)} tasks still pending; rerun with --download.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
