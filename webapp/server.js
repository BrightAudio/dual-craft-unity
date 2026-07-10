import { createServer } from "node:http";
import { readFileSync, existsSync, statSync } from "node:fs";
import { extname, join, normalize } from "node:path";
import { WebSocketServer } from "ws";

const ROOT = process.cwd();
const PUBLIC = join(ROOT, "public");
const REPO = join(ROOT, "..");
const CARD_DATA = JSON.parse(readFileSync(join(REPO, "Assets/Resources/CardData/all_cards.json"), "utf8")).cards;
const PORT = Number(process.env.PORT || 8787);
const LAN_HOST = "0.0.0.0";
const LANES = 5;

const mime = {
  ".html": "text/html; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".png": "image/png",
  ".jpg": "image/jpeg",
  ".jpeg": "image/jpeg",
  ".svg": "image/svg+xml",
};

const rooms = new Map();

function normalizeCard(card) {
  const category = card.category === "ashecard" ? "source"
    : card.category === "seal" ? "hex"
    : card.category === "pillar" ? "relic"
    : card.category;
  return {
    id: card.id,
    name: card.name,
    category,
    rarity: card.rarity || "common",
    element: card.element || "",
    creatureType: card.creatureType || "",
    life: Number(card.ashe || card.life || card.hp || 0),
    attack: Number(card.attack || 0),
    cost: Number(card.asheCost || card.willCost || 0),
    sePerTurn: category === "source" ? Number(card.sePerTurn || card.sourceSePerTurn || 2) : 0,
    description: card.description || card.ability?.description || "",
    flavorText: card.flavorText || "",
  };
}

const cards = CARD_DATA.map(normalizeCard).filter(card => card.id && card.name);
const byId = new Map(cards.map(card => [card.id, card]));

function roomCode() {
  const chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
  let code = "";
  do {
    code = Array.from({ length: 4 }, () => chars[Math.floor(Math.random() * chars.length)]).join("");
  } while (rooms.has(code));
  return code;
}

function choose(pool, count) {
  const result = [];
  if (!pool.length) return result;
  for (let i = 0; i < count; i++) result.push(pool[i % pool.length]);
  return result;
}

function buildDeck(archetype = "elemental") {
  const daemons = cards.filter(c => c.category === "daemon" && (!archetype || c.creatureType === archetype || c.element));
  const sources = cards.filter(c => c.category === "source");
  const hexes = cards.filter(c => c.category === "hex");
  const dispels = cards.filter(c => c.category === "dispel");
  const relics = cards.filter(c => c.category === "relic");
  const domains = cards.filter(c => c.category === "domain");
  const deck = [
    ...choose(daemons, 22),
    ...choose(sources, 18),
    ...choose(hexes, 7),
    ...choose(dispels, 5),
    ...choose(relics, 5),
    ...choose(domains, 3),
  ].map(c => c.id);
  return shuffle(deck);
}

function shuffle(deck) {
  const copy = [...deck];
  for (let i = copy.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [copy[i], copy[j]] = [copy[j], copy[i]];
  }
  return copy;
}

function newPlayer(name, seat) {
  const deck = buildDeck(seat === 0 ? "elemental" : "machine");
  return {
    name: name?.trim() || `Invoker ${seat + 1}`,
    hp: 30,
    se: 0,
    deck,
    hand: [],
    void: [],
    sources: [],
    field: Array.from({ length: LANES }, () => null),
    sourcePlayed: false,
  };
}

function draw(player, count = 1) {
  for (let i = 0; i < count; i++) {
    if (!player.deck.length) return;
    player.hand.push(player.deck.shift());
  }
}

function start(room) {
  room.started = true;
  room.turn = 0;
  room.phase = "main";
  room.log = ["Duel started.", `${room.players[0].name} takes first turn.`];
  for (const player of room.players) {
    draw(player, 5);
  }
  gainSources(room.players[0]);
}

function gainSources(player) {
  let gained = 0;
  for (const id of player.sources) gained += byId.get(id)?.sePerTurn || 2;
  player.se += gained;
  return gained;
}

function publicCard(id) {
  const c = byId.get(id);
  return c ? { ...c } : null;
}

function fieldCard(obj) {
  if (!obj) return null;
  return { ...obj, card: publicCard(obj.id) };
}

function view(room, seat) {
  return {
    code: room.code,
    started: room.started,
    you: seat,
    turn: room.turn,
    phase: room.phase,
    log: room.log.slice(-8),
    players: room.players.map((p, idx) => p ? ({
      name: p.name,
      hp: p.hp,
      se: p.se,
      deck: p.deck.length,
      void: p.void.length,
      sources: p.sources.map(publicCard),
      hand: idx === seat ? p.hand.map(publicCard) : Array.from({ length: p.hand.length }, () => null),
      field: p.field.map(fieldCard),
      sourcePlayed: p.sourcePlayed,
    }) : {
      name: "Waiting...",
      hp: 30,
      se: 0,
      deck: 0,
      void: 0,
      sources: [],
      hand: [],
      field: Array.from({ length: LANES }, () => null),
      sourcePlayed: false,
    }),
  };
}

function send(ws, type, payload) {
  if (ws.readyState === ws.OPEN) ws.send(JSON.stringify({ type, payload }));
}

function broadcast(room) {
  room.sockets.forEach((ws, seat) => send(ws, "state", view(room, seat)));
}

function fail(ws, message) {
  send(ws, "toast", { message });
}

function requireTurn(room, seat, ws) {
  if (!room.started) return fail(ws, "Waiting for both phones."), false;
  if (room.turn !== seat) return fail(ws, "Opponent's turn."), false;
  return true;
}

function playCard(room, seat, ws, handIndex, lane) {
  if (!requireTurn(room, seat, ws)) return;
  const p = room.players[seat];
  const id = p.hand[handIndex];
  const card = byId.get(id);
  if (!card) return fail(ws, "Card not found.");
  if (card.category === "source") {
    if (p.sourcePlayed) return fail(ws, "One Source per turn.");
    p.hand.splice(handIndex, 1);
    p.sources.push(id);
    p.sourcePlayed = true;
    room.log.push(`${p.name} set ${card.name}.`);
    broadcast(room);
    return;
  }
  if (card.category !== "daemon") return fail(ws, "This web test currently plays Source and Daemon cards.");
  if (lane < 0 || lane >= LANES || p.field[lane]) return fail(ws, "Pick an empty lane.");
  if (p.se < card.cost) return fail(ws, `Need ${card.cost} SE.`);
  p.se -= card.cost;
  p.hand.splice(handIndex, 1);
  p.field[lane] = { id, life: Math.max(1, card.life), maxLife: Math.max(1, card.life), ready: true };
  room.log.push(`${p.name} summoned ${card.name} in lane ${lane + 1}.`);
  broadcast(room);
}

function attack(room, seat, ws, lane, targetLane) {
  if (!requireTurn(room, seat, ws)) return;
  const p = room.players[seat];
  const o = room.players[1 - seat];
  const attacker = p.field[lane];
  if (!attacker) return fail(ws, "No daemon in that lane.");
  if (!attacker.ready) return fail(ws, "That daemon already attacked.");
  const card = byId.get(attacker.id);
  if (p.se < card.cost) return fail(ws, `Need ${card.cost} SE to attack.`);
  p.se -= card.cost;
  attacker.ready = false;
  const target = o.field[targetLane];
  if (target) {
    target.life -= card.attack;
    room.log.push(`${card.name} hit ${byId.get(target.id)?.name || "daemon"} for ${card.attack}.`);
    if (target.life <= 0) {
      o.void.push(target.id);
      o.field[targetLane] = null;
      o.hp -= rarityLoss(byId.get(target.id));
      room.log.push(`${byId.get(target.id)?.name || "Daemon"} shattered. ${o.name} lost life.`);
    }
  } else if (targetLane === lane) {
    o.hp -= card.attack;
    room.log.push(`${card.name} struck ${o.name} directly for ${card.attack}.`);
  } else {
    return fail(ws, "Direct attacks must be straight ahead.");
  }
  if (o.hp <= 0) {
    room.phase = "gameover";
    room.log.push(`${p.name} wins.`);
  }
  broadcast(room);
}

function rarityLoss(card) {
  return { common: 1, uncommon: 2, rare: 3, epic: 4, legendary: 5 }[card?.rarity] || 1;
}

function endTurn(room, seat, ws) {
  if (!requireTurn(room, seat, ws)) return;
  room.turn = 1 - room.turn;
  const next = room.players[room.turn];
  next.sourcePlayed = false;
  next.field.forEach(d => { if (d) d.ready = true; });
  draw(next, 1);
  const gained = gainSources(next);
  room.log.push(`${next.name} starts turn and gains ${gained} SE.`);
  broadcast(room);
}

const server = createServer((req, res) => {
  const url = new URL(req.url, `http://${req.headers.host}`);
  if (url.pathname === "/api/cards") {
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(JSON.stringify({ cards }));
    return;
  }
  if (url.pathname.startsWith("/art/")) {
    const id = url.pathname.slice("/art/".length).replace(/[^a-zA-Z0-9_.-]/g, "");
    const artPath = join(REPO, "Assets/Resources/CardArt", id);
    if (existsSync(artPath)) {
      res.writeHead(200, { "Content-Type": mime[extname(artPath)] || "application/octet-stream" });
      res.end(readFileSync(artPath));
      return;
    }
  }
  let file = url.pathname === "/" ? "index.html" : url.pathname.slice(1);
  file = normalize(file).replace(/^(\.\.[/\\])+/, "");
  const path = join(PUBLIC, file);
  if (!path.startsWith(PUBLIC) || !existsSync(path) || !statSync(path).isFile()) {
    res.writeHead(404);
    res.end("Not found");
    return;
  }
  res.writeHead(200, { "Content-Type": mime[extname(path)] || "application/octet-stream" });
  res.end(readFileSync(path));
});

const wss = new WebSocketServer({ server });
wss.on("connection", ws => {
  ws.on("message", raw => {
    let msg;
    try { msg = JSON.parse(raw); } catch { return; }
    if (msg.type === "host") {
      const code = roomCode();
      const room = { code, players: [newPlayer(msg.name, 0), null], sockets: new Map([[0, ws]]), started: false, turn: 0, phase: "waiting", log: [] };
      rooms.set(code, room);
      ws.roomCode = code; ws.seat = 0;
      send(ws, "hosted", { code });
      send(ws, "state", view(room, 0));
      return;
    }
    if (msg.type === "join") {
      const code = String(msg.code || "").trim().toUpperCase();
      const room = rooms.get(code);
      if (!room || room.players[1]) return fail(ws, "Room not found or already full.");
      room.players[1] = newPlayer(msg.name, 1);
      room.sockets.set(1, ws);
      ws.roomCode = code; ws.seat = 1;
      start(room);
      broadcast(room);
      return;
    }
    const room = rooms.get(ws.roomCode);
    if (!room) return fail(ws, "No room.");
    if (msg.type === "play") playCard(room, ws.seat, ws, Number(msg.handIndex), Number(msg.lane));
    if (msg.type === "attack") attack(room, ws.seat, ws, Number(msg.lane), Number(msg.targetLane));
    if (msg.type === "end") endTurn(room, ws.seat, ws);
  });
  ws.on("close", () => {
    const room = rooms.get(ws.roomCode);
    if (!room) return;
    room.log.push("Opponent disconnected.");
    broadcast(room);
  });
});

server.listen(PORT, LAN_HOST, () => {
  console.log(`DualMon phone web app running on http://localhost:${PORT}`);
});
