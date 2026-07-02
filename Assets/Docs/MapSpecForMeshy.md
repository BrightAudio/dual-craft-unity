Meshy — Map spec / refinement notes

Overview
- Purpose: create or refine the overworld map used by StorySceneController for player navigation and wild daemon encounters.
- Target art style: low-poly, stylized nature (matching Free Low Poly Nature Pack).
- Tile resolution: 1 unit = 1 tile; recommended Unity Terrain grid or Tilemap with 32x32 sprite tiles for 1:1 mapping.

Key gameplay areas
- Town tiles: walkable paths, path tile variants (cobble, dirt), spawn friendly NPCs and invokers.
- Grass/field tiles: default wild encounter tiles. Place environmental props (rocks, trees).
- Water tiles: water-themed battleback and different enemy pool.
- Academy/indoor tiles: tight indoor layout for trial encounters.

Encounter design
- Wild daemon spawn: mark spawn zones with metadata (tile tag "WildSpawn").
- Encounter density: default 1 spawn per 12 tiles in open areas; reduce to 1 per 24 in towns.
- Battleback themes: map tiles should map to battlebackThemeId values: grass, water, city, indoor1.

Technical notes
- Export layout as a Unity scene or a JSON layout file listing tile types and regions.
- Provide collision bounds and navigation polygon for the player controller.
- If using Tilemap: add a secondary Tilemap layer for metadata objects (Invokers, WildSpawn, Props).

Deliverables
- One refined Unity scene or Tilemap asset representing the main overworld area used in the demo (OutdoorsScene/Town).
- Optional: a simple prefab set of props sized to the grid.
- Notes on how tiles map to battlebackThemeId values.

Contact
- If you need mockups, I can provide a simple ASCII layout or a sample JSON tilemap to start.

