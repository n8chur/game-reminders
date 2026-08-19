import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

async function render() {
  const workerUrl = new URL("../dist/server/index.js", import.meta.url);
  workerUrl.searchParams.set("test", `${process.pid}-${Date.now()}`);
  const { default: worker } = await import(workerUrl.href);

  return worker.fetch(
    new Request("https://game-reminders.example/", {
      headers: { accept: "text/html", host: "game-reminders.example" },
    }),
    { ASSETS: { fetch: async () => new Response("Not found", { status: 404 }) } },
    { waitUntil() {}, passThroughOnException() {} },
  );
}

test("renders the Game Reminders landing page and release downloads", async () => {
  const response = await render();
  assert.equal(response.status, 200);
  assert.match(response.headers.get("content-type") ?? "", /^text\/html\b/i);

  const html = await response.text();
  assert.match(html, /Game Reminders — Remember when you launch/);
  assert.match(html, /Alpha · v0\.0\.2/);
  assert.match(html, /Download installer/);
  assert.match(html, /Download portable/);
  assert.match(html, /Download Game Reminder Shortcut/);
  assert.match(html, /Download Game-Reminder\.shortcut from the v0\.0\.2 release/);
  assert.match(html, /releases\/download\/v0\.0\.2\/Game-Reminder\.shortcut/);
  assert.match(html, /Get iCloud for Windows/);
  assert.match(html, /9PKTQ5699M62/);
  assert.match(html, /Install iCloud for Windows so those files can sync to your PC/);
  assert.match(html, /GameReminders-0\.0\.2-win-x64-setup\.exe/);
  assert.match(html, /GameReminders-0\.0\.2-win-x64-portable\.zip/);
  assert.match(html, /releases\/tag\/v0\.0\.2/);
  assert.doesNotMatch(html, /v0\.0\.1/);
  assert.match(html, /A tiny utility with one job/);
  assert.match(html, /From download to first reminder/);
  assert.doesNotMatch(html, /Example game reminder/);
  assert.doesNotMatch(html, /coming soon/i);
  assert.doesNotMatch(html, /codex-preview/);
  assert.match(html, /https:\/\/game-reminders\.example\/og\.png/);
});

test("uses the system dark-mode preference", async () => {
  const css = await readFile(new URL("../app/globals.css", import.meta.url), "utf8");
  assert.match(css, /@media \(prefers-color-scheme: dark\)/);
  assert.match(css, /color-scheme: dark/);
  assert.match(css, /--paper: #121116/);
});
