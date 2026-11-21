// The Lucent Discord bot, as a Cloudflare Worker. It answers Discord's slash commands over HTTPS, so it needs no always-on server.
//   /crosshair code:<LCX1-...>  shows a picture of a Lucent crosshair code and what is in it
//   /download                   links the newest Lucent release
// Secrets (set with `wrangler secret put`): DISCORD_PUBLIC_KEY. Nothing else is needed to run.
import { decodeCrosshair, describeCrosshair, renderCrosshairPng } from "./crosshair.js";

const SILVER = 0xc9cdd4;
const REPO = "getlucent/lucent";

const hex = (text) => Uint8Array.from(text.match(/../g) || [], (h) => parseInt(h, 16));

async function verifyDiscord(request, body, publicKey) {
  const signature = request.headers.get("x-signature-ed25519"), timestamp = request.headers.get("x-signature-timestamp");
  if (!signature || !timestamp || !publicKey) return false;
  const data = new TextEncoder().encode(timestamp + body);
  for (const algorithm of [{ name: "Ed25519" }, { name: "NODE-ED25519", namedCurve: "NODE-ED25519" }]) {
    try {
      const key = await crypto.subtle.importKey("raw", hex(publicKey), algorithm, false, ["verify"]);
      return await crypto.subtle.verify(algorithm.name, key, hex(signature), data);
    } catch (e) { /* try the next name */ }
  }
  return false;
}

const json = (value, status = 200) => new Response(JSON.stringify(value), { status, headers: { "content-type": "application/json" } });
const reply = (data) => json({ type: 4, data });
const privately = (content) => reply({ content, flags: 64 });

function optionValue(interaction, name) {
  const option = (interaction.data.options || []).find((o) => o.name === name);
  return option ? String(option.value) : "";
}

async function crosshairCommand(interaction, origin) {
  const code = optionValue(interaction, "code").trim();
  const crosshair = decodeCrosshair(code);
  if (!crosshair) return privately("That is not a Lucent crosshair code. In Lucent, open Crosshair, then Crosshair codes, then Copy my code. It starts with LCX1.");
  return reply({
    embeds: [{
      title: crosshair.name,
      color: SILVER,
      description: describeCrosshair(crosshair).join("\n") + "\n\n```\n" + code + "\n```",
      image: { url: `${origin}/preview.png?code=${encodeURIComponent(code)}` },
      footer: { text: "Import it in Lucent: Crosshair, Crosshair codes, Import" },
    }],
  });
}

async function downloadCommand() {
  let version = "";
  try {
    const r = await fetch(`https://api.github.com/repos/${REPO}/releases/latest`, { headers: { "user-agent": "lucent-discord-bot", accept: "application/vnd.github+json" } });
    if (r.ok) version = (await r.json()).name || "";
  } catch (e) { /* the link below still works */ }
  return reply({
    embeds: [{
      title: version ? `Download ${version}` : "Download Lucent",
      url: `https://github.com/${REPO}/releases/latest`,
      color: SILVER,
      description: "Free and open source, for Windows.",
    }],
  });
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (request.method === "GET" && url.pathname === "/preview.png") {
      const crosshair = decodeCrosshair(url.searchParams.get("code") || "");
      if (!crosshair) return new Response("Not a Lucent crosshair code", { status: 400 });
      return new Response(await renderCrosshairPng(crosshair), { headers: { "content-type": "image/png", "cache-control": "public, max-age=86400" } });
    }

    if (request.method === "POST") {
      const body = await request.text();
      if (!(await verifyDiscord(request, body, env.DISCORD_PUBLIC_KEY))) return new Response("Bad request signature", { status: 401 });
      const interaction = JSON.parse(body);
      if (interaction.type === 1) return json({ type: 1 });   // Discord's check that the address works
      if (interaction.type === 2) {
        if (interaction.data.name === "crosshair") return crosshairCommand(interaction, url.origin);
        if (interaction.data.name === "download") return downloadCommand();
      }
      return privately("Unknown command.");
    }

    return new Response("Lucent bot", { headers: { "content-type": "text/plain" } });
  },
};
