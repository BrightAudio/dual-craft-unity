let ws;
let state;
let selectedHand = null;
let selectedLane = null;

const $ = id => document.getElementById(id);
const lobby = $("lobby");
const battle = $("battle");

function connect() {
  if (ws && ws.readyState <= 1) return ws;
  ws = new WebSocket(`${location.protocol === "https:" ? "wss" : "ws"}://${location.host}`);
  ws.addEventListener("message", event => {
    const msg = JSON.parse(event.data);
    if (msg.type === "hosted") $("roomLine").textContent = `Room code: ${msg.payload.code}`;
    if (msg.type === "state") {
      state = msg.payload;
      render();
    }
    if (msg.type === "toast") toast(msg.payload.message);
  });
  ws.addEventListener("close", () => toast("Disconnected. Refresh to reconnect."));
  return ws;
}

function send(type, payload = {}) {
  const socket = connect();
  const fire = () => socket.send(JSON.stringify({ type, ...payload }));
  if (socket.readyState === WebSocket.OPEN) fire();
  else socket.addEventListener("open", fire, { once: true });
}

$("host").onclick = () => send("host", { name: $("name").value });
$("join").onclick = () => send("join", { name: $("name").value, code: $("code").value });
$("endTurn").onclick = () => {
  selectedHand = null;
  selectedLane = null;
  send("end");
};

function render() {
  if (!state) return;
  lobby.classList.add("hidden");
  battle.classList.remove("hidden");
  const you = state.players[state.you];
  const enemy = state.players[1 - state.you];
  $("roomCode").textContent = `Room ${state.code}`;
  $("turn").textContent = state.phase === "gameover" ? "Game Over" : state.turn === state.you ? "Your turn" : "Their turn";
  renderPlayer($("enemy"), enemy, false);
  renderPlayer($("you"), you, true);
  renderField($("enemyField"), enemy.field, false);
  renderField($("yourField"), you.field, true);
  renderHand(you.hand);
  $("log").innerHTML = state.log.map(line => `<div>${escapeHtml(line)}</div>`).join("");
}

function renderPlayer(el, p, isYou) {
  el.innerHTML = `
    <strong>${escapeHtml(p.name)}${isYou ? " (You)" : ""}</strong>
    <span class="badge hp">${p.hp} HP</span>
    <span class="badge se">${p.se} SE</span>
    <span class="badge">${p.deck} deck</span>
  `;
}

function renderField(el, field, isYou) {
  el.innerHTML = "";
  field.forEach((daemon, lane) => {
    const slot = document.createElement("button");
    slot.className = `slot ${selectedLane === lane && isYou ? "selected" : ""}`;
    slot.onclick = () => {
      if (selectedHand !== null && isYou) {
        send("play", { handIndex: selectedHand, lane });
        selectedHand = null;
        return;
      }
      if (isYou && daemon) {
        selectedLane = selectedLane === lane ? null : lane;
        render();
        return;
      }
      if (!isYou && selectedLane !== null) {
        send("attack", { lane: selectedLane, targetLane: lane });
        selectedLane = null;
      }
    };
    slot.innerHTML = daemon ? cardHtml(daemon.card, daemon) : "";
    el.appendChild(slot);
  });
}

function renderHand(hand) {
  const el = $("hand");
  el.innerHTML = "";
  hand.forEach((card, i) => {
    const btn = document.createElement("button");
    btn.className = `card ${card.category === "source" ? "sourceGlow" : ""} ${selectedHand === i ? "selected" : ""}`;
    btn.innerHTML = cardHtml(card);
    btn.onclick = () => {
      selectedLane = null;
      if (card.category === "source") {
        send("play", { handIndex: i, lane: -1 });
        return;
      }
      selectedHand = selectedHand === i ? null : i;
      toast(card.category === "daemon" ? "Tap an empty lane." : "This web test plays Source and Daemon cards.");
      render();
    };
    el.appendChild(btn);
  });
}

function cardHtml(card, fieldState = null) {
  if (!card) return "";
  const img = `/art/${encodeURIComponent(card.id)}.png`;
  const stats = card.category === "daemon"
    ? `<span>${fieldState ? fieldState.life : card.life} HP</span><span>${card.attack} ATK</span><span>${card.cost} SE</span>`
    : card.category === "source"
      ? `<span>+${card.sePerTurn} SE</span><span>Source</span>`
      : `<span>${escapeHtml(card.category)}</span>`;
  return `
    <img src="${img}" alt="" onerror="this.style.display='none'">
    <span class="plate">
      <span class="name">${escapeHtml(card.name)}</span>
      <span>${escapeHtml(label(card))}</span>
      <span class="stats">${stats}</span>
    </span>
  `;
}

function label(card) {
  const parts = [];
  if (card.element) parts.push(card.element);
  if (card.creatureType) parts.push(card.creatureType);
  parts.push(card.category);
  return parts.join(" ");
}

function toast(message) {
  const el = $("toast");
  el.textContent = message;
  el.style.display = "flex";
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => el.style.display = "none", 1800);
}

function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>"']/g, ch => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#039;",
  }[ch]));
}

connect();
