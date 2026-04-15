#!/usr/bin/env python3
"""
Dual Craft — Pokémon-Style AI Asset Generator via Meshy
========================================================
Generates ALL game art (card art + UI + backgrounds) in a bright,
colorful Pokémon/Pocket Monsters illustration style.
"""

import os, sys, json, time, urllib.request, urllib.error, ssl
from collections import OrderedDict

PROJECT_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CARD_ART_DIR = os.path.join(PROJECT_ROOT, "Assets", "Resources", "CardArt")
UI_DIR = os.path.join(PROJECT_ROOT, "Assets", "Resources", "UI")
TASK_CACHE = os.path.join(PROJECT_ROOT, "scripts", ".meshy_pokemon_tasks.json")
API_BASE = "https://api.meshy.ai/openapi/v1"

# Universal style suffix for Pokémon look
PKM = "vibrant colorful Pokémon TCG card art style, cel-shaded, clean bold lines, bright saturated colors, anime-inspired, Ken Sugimori illustration style, dynamic pose, high detail"

# ─── CARD ART PROMPTS (117 cards + card-back) ──────────
CARD_PROMPTS = OrderedDict()

# === DAEMONS (59 cards) ===
# Flame daemons
CARD_PROMPTS["d-flame-1"] = ("Emberclaw Drake — a fierce fire dragon with amber scales and blazing clawed wings, roaring fireball attack, " + PKM, "3:4")
CARD_PROMPTS["d-flame-2"] = ("Forge Sentinel — a hulking armored robot powered by an internal furnace, steam vents and glowing coal eyes, fire-type machine creature, " + PKM, "3:4")
CARD_PROMPTS["d-flame-3"] = ("Cindershade Wraith — a ghostly skeletal figure wreathed in blue-purple fire, undead flame spirit hovering above embers, " + PKM, "3:4")
CARD_PROMPTS["d-flame-4"] = ("Volcanic Spitter — a squat round fire lizard with glowing magma belly, spitting lava rocks from its mouth, cute but fierce, " + PKM, "3:4")
CARD_PROMPTS["d-flame-5"] = ("Ash Golem — an artificial construct made of hardened volcanic ash and obsidian, glowing orange rune markings, stoic guardian pose, " + PKM, "3:4")
CARD_PROMPTS["d-flame-6"] = ("Wildfire Fox — a graceful nine-tailed fox made of living flame, bright orange and gold fur with fire wisps, playful spirit creature, " + PKM, "3:4")
CARD_PROMPTS["d-flame-7"] = ("Magma Forgemaster — a massive mechanical blacksmith creature with molten metal arms, anvil chest plate, fire-type machine, " + PKM, "3:4")
CARD_PROMPTS["d-fox-fledgling"] = ("Fledgling Fox — a tiny baby fox kit with a single flickering flame tail, big round eyes, adorable fire type starter creature, " + PKM, "3:4")
CARD_PROMPTS["d-fox-grey"] = ("The Grey Fox — a mysterious dark-furred fox with shadow wisps, glowing purple eyes, sleek and cunning, dark spirit type, " + PKM, "3:4")

# Ice daemons
CARD_PROMPTS["d-ice-1"] = ("Frostfang Serpent — a massive ice cobra with crystalline scales and frost breath, glowing ice blue eyes, ice elemental, " + PKM, "3:4")
CARD_PROMPTS["d-ice-2"] = ("Glacier Golem — a towering ice robot with frozen armor plating and glowing blue core, mechanical ice creature, " + PKM, "3:4")
CARD_PROMPTS["d-ice-3"] = ("Phantom Frost — a ghostly spirit made of swirling snowflakes and ice crystals, translucent ethereal form, floating ice spirit, " + PKM, "3:4")
CARD_PROMPTS["d-ice-4"] = ("Rime Stalker — a skeletal undead wolf covered in frost and icicles, glowing blue eye sockets, frozen undead beast, " + PKM, "3:4")
CARD_PROMPTS["d-ice-5"] = ("Avalanche Wurm — a colossal ice worm bursting through glacial ice, crystalline spikes along body, ice elemental, " + PKM, "3:4")
CARD_PROMPTS["d-ice-6"] = ("Hailstone Crab — an armored mechanical crab with ice crystal claws, chrome and ice blue plating, machine ice creature, " + PKM, "3:4")
CARD_PROMPTS["d-ice-7"] = ("Blizzard Banshee — a beautiful ghostly woman in flowing ice robes, screaming with ice shards radiating outward, ice spirit, " + PKM, "3:4")
CARD_PROMPTS["d-fenrir"] = ("Fenrir the Great White Wolf — a legendary massive white wolf wreathed in ice storms, ancient and powerful, glowing blue aura, mythical ice spirit, " + PKM, "3:4")

# Water daemons
CARD_PROMPTS["d-water-1"] = ("Tidecaller Leviathan — a majestic sea dragon emerging from ocean waves, shimmering teal scales, water elemental, " + PKM, "3:4")
CARD_PROMPTS["d-water-2"] = ("Coral Automaton — a colorful robot made of living coral and sea shells, bubble jets, water-type machine creature, " + PKM, "3:4")
CARD_PROMPTS["d-water-3"] = ("Drowned Revenant — a waterlogged undead pirate skeleton with glowing green eyes, seaweed and barnacles, water undead, " + PKM, "3:4")
CARD_PROMPTS["d-water-4"] = ("Riptide Eel — a sleek electric eel creature crackling with water energy, bioluminescent stripes, water elemental, " + PKM, "3:4")
CARD_PROMPTS["d-water-5"] = ("Abyssal Kraken — a massive deep-sea squid spirit with glowing purple tentacles, mysterious deep ocean spirit, " + PKM, "3:4")
CARD_PROMPTS["d-water-6"] = ("Barnacle Drone — a small mechanical submarine-shaped creature covered in barnacles, propeller tail, water machine, " + PKM, "3:4")
CARD_PROMPTS["d-water-7"] = ("Monsoon Shaman — a wise frog shaman surrounded by floating water orbs, rain clouds above, water spirit, " + PKM, "3:4")

# Earth daemons
CARD_PROMPTS["d-earth-1"] = ("Stonecrest Titan — a colossal rock golem made of layered stone and crystals, glowing gem eyes, earth elemental, " + PKM, "3:4")
CARD_PROMPTS["d-earth-2"] = ("Root Weaver — a gentle earth spirit shaped like a walking tree stump, sprouting flowers and vines, earth spirit, " + PKM, "3:4")
CARD_PROMPTS["d-earth-3"] = ("Ironvein Golem — a mechanical mining robot with drill arms and ore-embedded chassis, heavy earth machine, " + PKM, "3:4")
CARD_PROMPTS["d-earth-4"] = ("Quake Beetle — a round armored beetle with crystal horn and stone shell, digging earth elemental, " + PKM, "3:4")
CARD_PROMPTS["d-earth-5"] = ("Terracotta Warrior — an ancient clay warrior construct with cracks revealing golden light, artificial earth guardian, " + PKM, "3:4")
CARD_PROMPTS["d-earth-6"] = ("Mudslide Toad — a big muddy toad spirit with glowing earth runes, forest guardian earth spirit, " + PKM, "3:4")
CARD_PROMPTS["d-earth-7"] = ("Crystal Borer — a mechanical tunneling mole machine with crystal drill nose, glowing ore veins, earth machine, " + PKM, "3:4")

# Air daemons
CARD_PROMPTS["d-air-1"] = ("Stormwing Raptor — a majestic eagle with lightning-charged feathers, stormy wings, air elemental bird, " + PKM, "3:4")
CARD_PROMPTS["d-air-2"] = ("Zephyr Djinn — a floating air spirit genie made of swirling wind and clouds, playful pose, air spirit, " + PKM, "3:4")
CARD_PROMPTS["d-air-3"] = ("Skyforge Eagle — a mechanical golden eagle with jet wings and radar eyes, air machine creature, " + PKM, "3:4")
CARD_PROMPTS["d-air-4"] = ("Gust Hawk — a swift small hawk made of living wind, transparent feathers of air, air elemental, " + PKM, "3:4")
CARD_PROMPTS["d-air-5"] = ("Thunderclap Monk — a floating monk spirit meditating on a cloud, lightning aura, peaceful air spirit, " + PKM, "3:4")
CARD_PROMPTS["d-air-6"] = ("Windmill Drake — a cute mechanical wind-powered dragon with spinning turbine wings, air machine, " + PKM, "3:4")
CARD_PROMPTS["d-air-7"] = ("Cyclone Djinn — a powerful tornado spirit with muscular wind form, spinning vortex body, air elemental, " + PKM, "3:4")

# Light daemons
CARD_PROMPTS["d-light-1"] = ("Radiant Phoenix — a brilliant golden phoenix with rainbow light wings, rebirth flames, light spirit creature, " + PKM, "3:4")
CARD_PROMPTS["d-light-2"] = ("Prism Automaton — a crystalline robot that refracts light into rainbows, geometric light machine, " + PKM, "3:4")
CARD_PROMPTS["d-light-3"] = ("Dawn Herald — a majestic sun lion with a golden mane made of light rays, light elemental, " + PKM, "3:4")
CARD_PROMPTS["d-light-4"] = ("Halo Sprite — a tiny cute fairy with a golden halo and sparkle wings, cheerful light spirit, " + PKM, "3:4")
CARD_PROMPTS["d-light-5"] = ("Solar Knight — an artificial knight construct of pure golden light, holy sword and shield, light artificial, " + PKM, "3:4")
CARD_PROMPTS["d-light-6"] = ("Beacon Turret — a lighthouse-shaped mechanical tower creature with rotating light beam, light machine, " + PKM, "3:4")
CARD_PROMPTS["d-light-7"] = ("Seraph of Reckoning — a six-winged celestial angel warrior with golden armor and divine light sword, light elemental, " + PKM, "3:4")

# Dark daemons
CARD_PROMPTS["d-dark-1"] = ("Voidmaw Beast — a massive shadow beast with a gaping void mouth and purple-black body, dark elemental, " + PKM, "3:4")
CARD_PROMPTS["d-dark-2"] = ("Shadow Reaper — a grim reaper skeleton in tattered robes with glowing scythe, dark undead, " + PKM, "3:4")
CARD_PROMPTS["d-dark-3"] = ("Null Construct — a sleek black geometric robot with void energy core, dark artificial construct, " + PKM, "3:4")
CARD_PROMPTS["d-dark-4"] = ("Gloom Crawler — a creepy spider-like undead creature with shadowy legs, dark undead, " + PKM, "3:4")
CARD_PROMPTS["d-dark-5"] = ("Nightmare Stalker — a shadowy panther spirit with glowing white eyes, stalking in darkness, dark spirit, " + PKM, "3:4")
CARD_PROMPTS["d-dark-6"] = ("Dusk Automaton — a dark mechanical knight with red glowing visor and shadow exhaust, dark machine, " + PKM, "3:4")
CARD_PROMPTS["d-dark-7"] = ("Abyssal Devourer — a massive void worm with rings of shadow teeth, consuming darkness, dark elemental, " + PKM, "3:4")

# Nature daemons
CARD_PROMPTS["d-nature-1"] = ("Vine Serpent — a bright green snake wrapped in flowering vines, nature elemental, " + PKM, "3:4")
CARD_PROMPTS["d-nature-2"] = ("Thorn Pixie — a tiny mischievous fairy with thorn wings and flower crown, nature spirit, " + PKM, "3:4")
CARD_PROMPTS["d-nature-3"] = ("Mushroom Sentry — a walking mushroom construct with dotted cap and spore shield, nature artificial, " + PKM, "3:4")
CARD_PROMPTS["d-nature-4"] = ("Elderwood Treant — a wise ancient tree creature with a face of bark, glowing green eyes, nature elemental, " + PKM, "3:4")
CARD_PROMPTS["d-nature-5"] = ("Blossom Shaman — a floral spirit deer with cherry blossom antlers and petal aura, nature spirit, " + PKM, "3:4")
CARD_PROMPTS["d-nature-6"] = ("Verdant Hydra — a multi-headed plant hydra with leafy necks and flower heads, nature elemental, " + PKM, "3:4")
CARD_PROMPTS["d-nature-7"] = ("Mycelium Network — a mechanical fungal colony robot with connected mushroom nodes, nature machine, " + PKM, "3:4")

# === CONJURORS (8 cards) ===
CARD_PROMPTS["cj-flame"] = ("Pyralis the Eternal Flame — powerful fire mage trainer with flowing red robes, flames orbiting hands, confident smile, " + PKM, "3:4")
CARD_PROMPTS["cj-ice"] = ("Crystalis the Frozen Mind — elegant ice mage trainer with frost crown, crystalline staff, cool composed expression, " + PKM, "3:4")
CARD_PROMPTS["cj-water"] = ("Tidecrest Lord of Currents — noble water mage trainer with ocean trident, wave cape, serene look, " + PKM, "3:4")
CARD_PROMPTS["cj-earth"] = ("Terravex the Living Mountain — mighty earth mage trainer with crystal-embedded armor, rocky fists, stoic, " + PKM, "3:4")
CARD_PROMPTS["cj-air"] = ("Zephyria Voice of Thunder — graceful wind mage trainer floating on clouds, lightning eyes, flowing white outfit, " + PKM, "3:4")
CARD_PROMPTS["cj-light"] = ("Seraph Solara the Radiant Judge — holy light mage trainer with golden halo, radiant armor, just expression, " + PKM, "3:4")
CARD_PROMPTS["cj-dark"] = ("Morvath the Abyssal Lord — imposing dark mage trainer with void cloak, purple energy, calculating eyes, " + PKM, "3:4")
CARD_PROMPTS["cj-nature"] = ("Gaia the World Soul — gentle nature mage trainer with vine-wrapped staff, floral dress, nurturing presence, " + PKM, "3:4")

# === PILLARS (26 cards) ===
CARD_PROMPTS["p-flame-1"] = ("Pillar of the Blazing Sun — a towering volcanic fire pillar erupting with solar energy, lava cascading, bright warm colors, " + PKM, "3:4")
CARD_PROMPTS["p-flame-2"] = ("Pillar of Infernal Gates — a hellish obsidian gateway pillar wreathed in fire, demonic runes glowing red, " + PKM, "3:4")
CARD_PROMPTS["p-ice-1"] = ("Pillar of the Frozen Throne — a grand ice crystal throne pillar radiating cold blue energy, snowflakes, " + PKM, "3:4")
CARD_PROMPTS["p-ice-2"] = ("Pillar of Eternal Winter — a perpetual blizzard pillar of ice and snow, frozen landscape, pale blue, " + PKM, "3:4")
CARD_PROMPTS["p-water-1"] = ("Pillar of the Storm's Eye — a swirling water pillar with storm clouds above and ocean waves, aqua blue, " + PKM, "3:4")
CARD_PROMPTS["p-water-2"] = ("Pillar of the Celestial Wind — a sky-reaching water and wind pillar with cloud spirals, ethereal blue, " + PKM, "3:4")
CARD_PROMPTS["p-earth-1"] = ("Pillar of the World Tree — a massive living tree pillar with spreading roots and golden leaves, " + PKM, "3:4")
CARD_PROMPTS["p-earth-2"] = ("Pillar of the Iron Mountain — a mountain peak pillar made of iron ore and crystal veins, " + PKM, "3:4")
CARD_PROMPTS["p-air-1"] = ("Pillar of stormy winds — a floating stone pillar in clouds with lightning, wind elemental, " + PKM, "3:4")
CARD_PROMPTS["p-air-2"] = ("Pillar of celestial clouds — a heavenly pillar among gentle clouds and sunbeams, air pillar, " + PKM, "3:4")
CARD_PROMPTS["p-light-1"] = ("Pillar of Divine Radiance — a pure golden light beam pillar ascending to heavens, holy sigils, " + PKM, "3:4")
CARD_PROMPTS["p-light-2"] = ("Pillar of the Morning Star — a brilliant star-shaped pillar with prismatic light, " + PKM, "3:4")
CARD_PROMPTS["p-dark-1"] = ("Pillar of the Void — a dark crystal pillar surrounded by swirling void energy, purple-black, " + PKM, "3:4")
CARD_PROMPTS["p-dark-2"] = ("Pillar of Endless Night — a shadowy pillar under a total eclipse, dark purple aura, " + PKM, "3:4")
CARD_PROMPTS["p-nature-1"] = ("Pillar of the Ancient Grove — a living moss-covered stone pillar in an enchanted forest, " + PKM, "3:4")
CARD_PROMPTS["p-nature-2"] = ("Pillar of the Verdant Canopy — a towering vine-wrapped pillar with flowering canopy, " + PKM, "3:4")
CARD_PROMPTS["p-elemental-1"] = ("Pillar of the Primal Storm — a raw elemental pillar of all four elements colliding, " + PKM, "3:4")
CARD_PROMPTS["p-elemental-2"] = ("Pillar of the Convergence — a pillar where water, fire, earth and wind converge in harmony, " + PKM, "3:4")
CARD_PROMPTS["p-spirit-1"] = ("Pillar of the Ethereal Gate — a ghostly translucent pillar portal to the spirit world, " + PKM, "3:4")
CARD_PROMPTS["p-spirit-2"] = ("Pillar of the Ancestral Veil — a dark misty pillar with ghostly ancestor faces, " + PKM, "3:4")
CARD_PROMPTS["p-machine-1"] = ("Pillar of the Iron Forge — a massive mechanical pillar of gears, pistons and molten metal, " + PKM, "3:4")
CARD_PROMPTS["p-machine-2"] = ("Pillar of the Cogwork Engine — a clockwork tower pillar with spinning gears and steam, " + PKM, "3:4")
CARD_PROMPTS["p-artificial-1"] = ("Pillar of the Architect — a sleek crystalline tech pillar with holographic runes, " + PKM, "3:4")
CARD_PROMPTS["p-artificial-2"] = ("Pillar of the Living Gallery — an art-gallery pillar with animated magical paintings, " + PKM, "3:4")
CARD_PROMPTS["p-undead-1"] = ("Pillar of the Bone Throne — a pillar of stacked skulls and bones with ghostly green fire, " + PKM, "3:4")
CARD_PROMPTS["p-undead-2"] = ("Pillar of the Plague Crypt — a rotting vine-covered crypt pillar with toxic purple mist, " + PKM, "3:4")

# === SPELLS (dispels, domains, masks, seals) ===
CARD_PROMPTS["disp-1"] = ("Purifying Light — a beam of holy golden light dispelling darkness and corruption, spell effect, " + PKM, "3:4")
CARD_PROMPTS["disp-2"] = ("Sacred Severance — a divine blade cutting through magical chains, spell effect, " + PKM, "3:4")
CARD_PROMPTS["disp-3"] = ("Seal Shatter — a magical seal cracking and exploding with energy, spell effect, " + PKM, "3:4")
CARD_PROMPTS["disp-4"] = ("Arcane Dissolution — swirling purple energy dissolving magical constructs, spell effect, " + PKM, "3:4")
CARD_PROMPTS["disp-5"] = ("Divine Judgment — a massive golden eye in the sky judging with holy light, spell effect, " + PKM, "3:4")
CARD_PROMPTS["disp-6"] = ("Null Tide — a wave of anti-magic water washing away enchantments, spell effect, " + PKM, "3:4")
CARD_PROMPTS["disp-7"] = ("Void Erasure — a black hole consuming all magic around it, spell effect, " + PKM, "3:4")

CARD_PROMPTS["f-1"] = ("Mana Surge — a burst of colorful mana energy erupting from a crystal, domain spell, " + PKM, "3:4")
CARD_PROMPTS["f-2"] = ("Ashe Storm — a swirling storm of magical ashe particles and energy, domain spell, " + PKM, "3:4")
CARD_PROMPTS["f-3"] = ("Sanctuary — a protective golden dome of light shielding a peaceful garden, domain spell, " + PKM, "3:4")
CARD_PROMPTS["f-4"] = ("Elemental Convergence — all eight elements swirling together in a colorful spiral, domain spell, " + PKM, "3:4")
CARD_PROMPTS["f-5"] = ("Dimensional Rift — a crack in reality showing another dimension with different colors, domain spell, " + PKM, "3:4")
CARD_PROMPTS["f-6"] = ("Pillar of Rebirth — a crumbling pillar being restored by golden light, domain spell, " + PKM, "3:4")
CARD_PROMPTS["f-7"] = ("Bastion Mending — cracked stone walls mending with flowing green healing energy, domain spell, " + PKM, "3:4")

CARD_PROMPTS["m-1"] = ("Mask of Fury — a fierce red battle mask with flame aura, radiating anger and power, " + PKM, "3:4")
CARD_PROMPTS["m-2"] = ("Mask of Iron — a heavy steel mask with rivets and gears, defensive and unbreakable, " + PKM, "3:4")
CARD_PROMPTS["m-3"] = ("Mask of Haste — a sleek wind-shaped mask with speed lines and lightning motifs, " + PKM, "3:4")
CARD_PROMPTS["m-4"] = ("Mask of Shadows — a dark purple mask dissolving into shadow wisps, mysterious, " + PKM, "3:4")
CARD_PROMPTS["m-5"] = ("Mask of Thorns — a wooden mask made of thorny vines with small flowers, poisonous, " + PKM, "3:4")

CARD_PROMPTS["s-1"] = ("a magical glowing golden seal rune circle floating in the air, seal spell, " + PKM, "3:4")
CARD_PROMPTS["s-2"] = ("a freezing ice seal with frost crystals forming a magic circle, seal spell, " + PKM, "3:4")
CARD_PROMPTS["s-3"] = ("a nature vine seal growing into a protective ring pattern, seal spell, " + PKM, "3:4")
CARD_PROMPTS["s-4"] = ("a dark void seal pulling energy inward, purple and black magic circle, seal spell, " + PKM, "3:4")
CARD_PROMPTS["s-5"] = ("an ancient stone seal with glowing earth runes activating, seal spell, " + PKM, "3:4")

# Card back
CARD_PROMPTS["card-back"] = ("A premium TCG card back design with a golden emblem on dark background, ornate border with elemental symbols (fire, ice, water, earth, air, light, dark, nature) arranged in a circle, luxurious and mysterious, " + PKM, "3:4")

# ─── UI / BATTLE BOARD PROMPTS ────────────────────────
UI_PROMPTS = OrderedDict()

# Battle backgrounds and boards
UI_PROMPTS["battle-bg"] = ("A wide Pokémon-style battle arena scene, colorful floating platform with elemental zones, bright skies with magical energy, top-down perspective board game surface, " + PKM, "16:9")
UI_PROMPTS["menu-bg"] = ("A beautiful Pokémon game title screen background, colorful floating island with magical creatures, bright warm sunset sky, welcoming and inviting, " + PKM, "16:9")
UI_PROMPTS["collection-bg"] = ("A Pokémon-style card collection room interior, bookshelves and display cases with colorful cards, warm cozy lighting, " + PKM, "16:9")
UI_PROMPTS["deckbuilder-bg"] = ("A Pokémon card workshop table scene, scattered colorful cards, crafting tools, warm wood desk, organized shelves, " + PKM, "16:9")
UI_PROMPTS["ceremony-bg"] = ("A Pokémon victory ceremony scene, bright fireworks and confetti, champion podium, celebratory golden light, " + PKM, "16:9")
UI_PROMPTS["packopen-bg"] = ("A magical card pack opening scene with burst of golden light and sparkles, colorful energy, exciting, " + PKM, "16:9")

# Card frames (portrait format to frame card art)
UI_PROMPTS["frame-daemon"] = ("A decorative TCG card frame border for creature cards, red and gold ornate edges with claw symbols, no center (transparent center), Pokémon card style frame, " + PKM, "3:4")
UI_PROMPTS["frame-pillar"] = ("A decorative TCG card frame border for structure cards, blue and silver ornate edges with stone motifs, no center, Pokémon card style frame, " + PKM, "3:4")
UI_PROMPTS["frame-conjuror"] = ("A decorative TCG card frame border for hero/trainer cards, gold and purple ornate edges with star symbols, no center, Pokémon card style frame, " + PKM, "3:4")
UI_PROMPTS["frame-spell"] = ("A decorative TCG card frame border for spell cards, teal and white ornate edges with magic circle motifs, no center, Pokémon card style frame, " + PKM, "3:4")
UI_PROMPTS["card-back-premium"] = ("A luxurious premium TCG card back with golden foil embossed emblem, dark velvet background, glowing elemental ring, collector edition style, " + PKM, "3:4")

# Battle zone areas
UI_PROMPTS["zone-daemon"] = ("A Pokémon battle zone surface for placing creature cards, colorful glowing grid with elemental energy, top-down view, bright arena floor, " + PKM, "16:9")
UI_PROMPTS["zone-pillar"] = ("A Pokémon stadium support zone surface for placing structure cards, stone platform with rune markings, top-down view arena, " + PKM, "16:9")
UI_PROMPTS["zone-hand"] = ("A Pokémon card game hand zone, translucent blue energy platform for holding cards, bottom of screen, " + PKM, "16:9")

# Pack designs
UI_PROMPTS["pack-inferno"] = ("A Pokémon-style booster pack wrapper design, fire theme with flame art, red and orange colors, exciting product packaging, " + PKM, "3:4")
UI_PROMPTS["pack-shadow"] = ("A Pokémon-style booster pack wrapper design, dark/shadow theme, purple and black colors, mysterious packaging, " + PKM, "3:4")
UI_PROMPTS["pack-verdant"] = ("A Pokémon-style booster pack wrapper design, nature theme with leaf art, green and gold colors, fresh packaging, " + PKM, "3:4")
UI_PROMPTS["pack-glow"] = ("A magical glowing energy effect for pack opening, golden sparkles and light rays radiating outward, " + PKM, "1:1")
UI_PROMPTS["pack-showcase-bg"] = ("A Pokémon card shop display shelf with spotlights, velvet display, premium showcase background, " + PKM, "16:9")

# Feature icons (used in menus)
UI_PROMPTS["feature-battle"] = ("A Pokémon-style battle mode icon, two creatures facing off with energy collision, bright dynamic, " + PKM, "1:1")
UI_PROMPTS["feature-decks"] = ("A Pokémon-style deck builder icon, stack of colorful cards with sparkle, organized, " + PKM, "1:1")
UI_PROMPTS["feature-grimoire"] = ("A Pokémon-style card collection book icon, magical tome with glowing pages, " + PKM, "1:1")
UI_PROMPTS["feature-shop"] = ("A Pokémon-style card shop icon, cute shop building with card packs displayed, welcoming, " + PKM, "1:1")
UI_PROMPTS["feature-story"] = ("A Pokémon-style adventure mode icon, winding path through colorful landscape, journey, " + PKM, "1:1")

# Health/stat icons
UI_PROMPTS["icon-heart"] = ("A bright red heart icon with a small sparkle, game health point symbol, clean simple design, " + PKM, "1:1")
UI_PROMPTS["icon-mana"] = ("A glowing blue crystal mana icon, game resource symbol, bright and clean, " + PKM, "1:1")
UI_PROMPTS["icon-shield"] = ("A shiny golden shield icon with star emblem, game defense symbol, " + PKM, "1:1")
UI_PROMPTS["icon-skull"] = ("A stylized cartoon skull icon with glowing eyes, game damage symbol, not too scary, " + PKM, "1:1")
UI_PROMPTS["icon-sword"] = ("A bright silver sword icon with golden hilt, game attack symbol, dynamic angle, " + PKM, "1:1")

# UI panels and bars
UI_PROMPTS["panel-dark"] = ("A semi-transparent dark game UI panel background with subtle magical border glow, clean modern TCG interface, " + PKM, "16:9")
UI_PROMPTS["panel-header"] = ("A colorful game UI header bar with golden trim and gradient coloring, TCG interface header, " + PKM, "16:9")
UI_PROMPTS["header-dark"] = ("A dark game UI top bar with golden accents and star decorations, TCG interface, " + PKM, "16:9")
UI_PROMPTS["menu-gradient"] = ("A smooth colorful gradient background fading from deep blue to purple with sparkles, game menu backdrop, " + PKM, "16:9")
UI_PROMPTS["packopen-gradient"] = ("A radial gradient burst of golden light on dark background, pack reveal backdrop, " + PKM, "16:9")

# HP/energy bars
UI_PROMPTS["bar-player"] = ("A bright colorful Pokémon-style health bar for the player, green gradient with golden frame, game HUD element, " + PKM, "16:9")
UI_PROMPTS["bar-opponent"] = ("A Pokémon-style enemy health bar, red gradient with silver frame, game HUD element, " + PKM, "16:9")

# Buttons
UI_PROMPTS["btn-gold"] = ("A bright golden game button with embossed text area, shiny metallic, Pokémon game UI button, " + PKM, "3:1")
UI_PROMPTS["btn-red"] = ("A bright red game button with white border, glossy, Pokémon game UI button, " + PKM, "3:1")

# Misc UI
UI_PROMPTS["divider-gold"] = ("A thin horizontal golden ornamental divider line with small star in center, game UI separator, " + PKM, "16:1")
UI_PROMPTS["nav-bar-bg"] = ("A dark navigation bar background with subtle colorful gradient, game UI footer, " + PKM, "16:9")
UI_PROMPTS["config-bar-bg"] = ("A game settings bar background, dark with subtle gear pattern, TCG UI element, " + PKM, "16:9")
UI_PROMPTS["deck-panel-bg"] = ("A warm wooden panel background for deck display, card game table surface, " + PKM, "16:9")
UI_PROMPTS["detail-panel-bg"] = ("A detailed card inspection panel background, dark with spotlight effect, premium look, " + PKM, "16:9")
UI_PROMPTS["filter-bar-bg"] = ("A sleek dark filter toolbar background with subtle element icons, card game UI, " + PKM, "16:9")
UI_PROMPTS["search-input-bg"] = ("A rounded search input field background, light with subtle glow border, clean UI, " + PKM, "16:9")
UI_PROMPTS["pool-panel-bg"] = ("A card pool selection panel background, blue-tinted with card silhouettes, " + PKM, "16:9")


# ─── API FUNCTIONS ────────────────────────────────────
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
        with urllib.request.urlopen(req, context=ctx, timeout=30) as resp:
            return json.loads(resp.read().decode())
    except urllib.error.HTTPError as e:
        err_body = e.read().decode() if e.fp else ""
        print(f"  API Error {e.code}: {err_body[:200]}")
        return None
    except Exception as e:
        print(f"  Request error: {e}")
        return None


def load_cache():
    if os.path.exists(TASK_CACHE):
        with open(TASK_CACHE) as f:
            return json.load(f)
    return {}


def save_cache(cache):
    os.makedirs(os.path.dirname(TASK_CACHE), exist_ok=True)
    with open(TASK_CACHE, "w") as f:
        json.dump(cache, f, indent=2)


def submit_task(asset_id, prompt, aspect_ratio, api_key):
    data = {
        "ai_model": "nano-banana",
        "prompt": prompt,
        "aspect_ratio": aspect_ratio,
    }
    result = api_request("POST", "/text-to-image", data, api_key)
    if result and "result" in result:
        task_id = result["result"]
        print(f"  [SUBMIT] {asset_id} → task {task_id[:12]}...")
        return task_id
    print(f"  [FAIL] Could not submit {asset_id}")
    return None


def check_task(task_id, api_key):
    return api_request("GET", f"/text-to-image/{task_id}", api_key=api_key)


def download_image(url, output_path):
    ctx = ssl.create_default_context()
    req = urllib.request.Request(url)
    try:
        with urllib.request.urlopen(req, context=ctx, timeout=60) as resp:
            data = resp.read()
        os.makedirs(os.path.dirname(output_path), exist_ok=True)
        with open(output_path, "wb") as f:
            f.write(data)
        print(f"  [DONE] {os.path.basename(output_path)} ({len(data)//1024}KB)")
        return True
    except Exception as e:
        print(f"  [DOWNLOAD FAIL] {e}")
        return False


def get_output_path(asset_id, is_ui=False):
    if is_ui:
        return os.path.join(UI_DIR, f"{asset_id}.png")
    return os.path.join(CARD_ART_DIR, f"{asset_id}.png")


def run_batch(api_key, force=False):
    cache = load_cache()
    os.makedirs(CARD_ART_DIR, exist_ok=True)
    os.makedirs(UI_DIR, exist_ok=True)

    # Build combined task list: (asset_id, prompt, aspect_ratio, is_ui)
    all_tasks = []
    for aid, (prompt, ar) in CARD_PROMPTS.items():
        all_tasks.append((aid, prompt, ar, False))
    for aid, (prompt, ar) in UI_PROMPTS.items():
        all_tasks.append((aid, prompt, ar, True))

    # Filter to what needs work
    to_submit = []
    for aid, prompt, ar, is_ui in all_tasks:
        cached = cache.get(aid, {})
        if not force and cached.get("status") == "SUCCEEDED":
            out = get_output_path(aid, is_ui)
            if os.path.exists(out):
                continue
        if cached.get("status") in ("PENDING", "IN_PROGRESS"):
            continue  # already submitted, will poll
        to_submit.append((aid, prompt, ar, is_ui))

    # Count pending
    pending_in_cache = sum(1 for v in cache.values() if v.get("status") in ("PENDING", "IN_PROGRESS"))

    print(f"\n{'='*60}")
    print(f"  Dual Craft — Pokémon-Style AI Art Generator")
    print(f"{'='*60}")
    print(f"  Total assets: {len(all_tasks)} ({len(CARD_PROMPTS)} cards + {len(UI_PROMPTS)} UI)")
    print(f"  Already complete: {sum(1 for v in cache.values() if v.get('status')=='SUCCEEDED')}")
    print(f"  Pending in queue: {pending_in_cache}")
    print(f"  New to submit: {len(to_submit)}")
    print(f"{'='*60}\n")

    if not to_submit and not pending_in_cache:
        print("All assets generated! Nothing to do.")
        return

    # Submit new tasks (rate-limited)
    for i, (aid, prompt, ar, is_ui) in enumerate(to_submit):
        task_id = submit_task(aid, prompt, ar, api_key)
        if task_id:
            cache[aid] = {"task_id": task_id, "status": "PENDING", "is_ui": is_ui}
            save_cache(cache)
        if (i + 1) % 5 == 0:
            print(f"  ... submitted {i+1}/{len(to_submit)}, pausing 2s for rate limit")
            time.sleep(2)
        else:
            time.sleep(0.5)

    # Poll for results
    print(f"\n{'='*60}")
    print("  Polling for results (this may take several minutes)...")
    print(f"{'='*60}\n")

    max_polls = 120  # 10 minutes max
    for poll_round in range(max_polls):
        pending = {k: v for k, v in cache.items() if v.get("status") in ("PENDING", "IN_PROGRESS")}
        if not pending:
            break

        completed_this_round = 0
        for aid, info in list(pending.items()):
            result = check_task(info["task_id"], api_key)
            if not result:
                continue

            status = result.get("status", "UNKNOWN")
            cache[aid]["status"] = status

            if status == "SUCCEEDED":
                urls = result.get("image_urls", [])
                if urls:
                    is_ui = info.get("is_ui", aid in UI_PROMPTS)
                    out_path = get_output_path(aid, is_ui)
                    if download_image(urls[0], out_path):
                        completed_this_round += 1
            elif status == "FAILED":
                err = result.get("task_error", {}).get("message", "Unknown")
                print(f"  [FAILED] {aid}: {err}")

            time.sleep(0.3)  # Don't hammer the status endpoint

        save_cache(cache)

        done_total = sum(1 for v in cache.values() if v.get("status") == "SUCCEEDED")
        still_pending = sum(1 for v in cache.values() if v.get("status") in ("PENDING", "IN_PROGRESS"))
        failed = sum(1 for v in cache.values() if v.get("status") == "FAILED")

        if completed_this_round > 0 or poll_round % 6 == 0:
            print(f"  [Poll #{poll_round+1}] Done: {done_total} | Pending: {still_pending} | Failed: {failed}")

        if still_pending == 0:
            break

        time.sleep(5)

    # Final summary
    print(f"\n{'='*60}")
    print("  GENERATION COMPLETE")
    print(f"{'='*60}")
    done = sum(1 for v in cache.values() if v.get("status") == "SUCCEEDED")
    failed = sum(1 for v in cache.values() if v.get("status") == "FAILED")
    pending = sum(1 for v in cache.values() if v.get("status") in ("PENDING", "IN_PROGRESS"))
    print(f"  Completed: {done}")
    print(f"  Failed: {failed}")
    print(f"  Still pending: {pending}")
    if failed > 0:
        print("\n  Failed assets:")
        for k, v in cache.items():
            if v.get("status") == "FAILED":
                print(f"    - {k}")
    if pending > 0:
        print(f"\n  Run again to continue polling pending tasks.")


def check_status(api_key):
    cache = load_cache()
    if not cache:
        print("No tasks yet. Run without flags to generate.")
        return

    pending = {k: v for k, v in cache.items() if v.get("status") in ("PENDING", "IN_PROGRESS")}
    done = sum(1 for v in cache.values() if v.get("status") == "SUCCEEDED")
    failed = sum(1 for v in cache.values() if v.get("status") == "FAILED")

    print(f"Done: {done} | Pending: {len(pending)} | Failed: {failed}")

    # Check and download any newly completed
    for aid, info in list(pending.items()):
        result = check_task(info["task_id"], api_key)
        if not result:
            continue
        status = result.get("status", "UNKNOWN")
        cache[aid]["status"] = status
        if status == "SUCCEEDED":
            urls = result.get("image_urls", [])
            if urls:
                is_ui = info.get("is_ui", aid in UI_PROMPTS)
                out_path = get_output_path(aid, is_ui)
                download_image(urls[0], out_path)
        elif status == "FAILED":
            print(f"  [FAILED] {aid}")
        time.sleep(0.3)

    save_cache(cache)


if __name__ == "__main__":
    force = "--force" in sys.argv
    api_key = get_api_key()

    if "--status" in sys.argv:
        check_status(api_key)
    elif "--list" in sys.argv:
        cache = load_cache()
        for aid in list(CARD_PROMPTS) + list(UI_PROMPTS):
            st = cache.get(aid, {}).get("status", "MISSING")
            is_ui = aid in UI_PROMPTS
            out = get_output_path(aid, is_ui)
            exists = "✓" if os.path.exists(out) else "✗"
            print(f"  {exists} {aid:25s} {st}")
    else:
        run_batch(api_key, force=force)
