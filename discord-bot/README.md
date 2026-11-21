# Lucent Discord bot

A small bot for the Lucent Discord. It runs as a Cloudflare Worker, which is free and needs no card or always-on server.

- `/crosshair code:LCX1-...` shows a Lucent crosshair code as a picture (on a dark and a light background) and lists what is in it.
- `/download` links the newest Lucent release.

## Set it up (about 10 minutes)

1. **Make the bot.** Go to https://discord.com/developers/applications, press New Application, name it Lucent. On the General Information page copy the **Application ID** and the **Public Key**. Under Bot, press Reset Token and copy the **token** (keep it private; it never goes in a file or in the repo).
2. **Deploy the worker.** Install Node.js, then in this folder:
   ```
   npx wrangler login
   npx wrangler deploy
   npx wrangler secret put DISCORD_PUBLIC_KEY
   ```
   Paste the Public Key when asked. `deploy` prints the worker's address, like `https://lucent-bot.YOURNAME.workers.dev`.
3. **Tell Discord where it is.** In the Developer Portal, General Information, paste that address into **Interactions Endpoint URL** and save. Discord checks it straight away.
4. **Register the commands** (once):
   ```
   set DISCORD_APP_ID=your application id
   set DISCORD_BOT_TOKEN=your bot token
   node register-commands.mjs
   ```
5. **Add it to the server.** Developer Portal, OAuth2, URL Generator: tick `applications.commands`, open the link, pick the Lucent server.

The worker only answers slash commands, so it cannot read messages. That is why it needs no intents and no token at run time.

## Release announcements

`.github/workflows/announce-release.yml` posts every published release to Discord. In the Lucent server make a webhook for the updates channel (Server Settings, Integrations, Webhooks, copy the URL), then on GitHub add it as a repository secret named `DISCORD_WEBHOOK_URL` (Settings, Secrets and variables, Actions).
