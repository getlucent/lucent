// Registers the bot's slash commands with Discord. Run it once (and again if you change the commands):
//   set DISCORD_APP_ID=...      (the Application ID from the Developer Portal)
//   set DISCORD_BOT_TOKEN=...   (the Bot token, kept on your PC, never put in a file)
//   node register-commands.mjs
const app = process.env.DISCORD_APP_ID, token = process.env.DISCORD_BOT_TOKEN;
if (!app || !token) { console.error("Set DISCORD_APP_ID and DISCORD_BOT_TOKEN first."); process.exit(1); }

const commands = [
  {
    name: "crosshair",
    description: "Show a Lucent crosshair code as a picture",
    options: [{ type: 3, name: "code", description: "The code from Lucent: Crosshair, Crosshair codes, Copy my code", required: true }],
  },
  { name: "download", description: "Get the newest version of Lucent" },
];

const response = await fetch(`https://discord.com/api/v10/applications/${app}/commands`, {
  method: "PUT",
  headers: { authorization: `Bot ${token}`, "content-type": "application/json" },
  body: JSON.stringify(commands),
});
console.log(response.status, response.ok ? "commands registered" : await response.text());
