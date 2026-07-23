# DualMon Dedicated Server

This target runs one authoritative two-player match. Unity Relay carries encrypted
packets, while the server owns decks, hidden hands, random outcomes, action
validation, and win state.

## Build

From Unity, run `Build > Build Linux Dedicated Server`, or use:

```sh
/Applications/Unity/Hub/Editor/6000.4.2f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -quit \
  -projectPath /Users/stephenalexanderbright/dual-craft-unity \
  -executeMethod BuildScript.BuildLinuxDedicatedServer
```

Set `DUALMON_SERVER_BUILD_PATH` to override the output path. The checked-in
Dockerfile downloads the pinned, checksummed protocol-v5 Linux build from the
GitHub release, so it can deploy directly from this repository:

```sh
docker build -t dualmon-server server
docker run --rm dualmon-server
```

The release archive and SHA-256 checksum are published at
`cloud-authority-v5-20260722`. To test a different server artifact, pass its URL
as `--build-arg SERVER_ARCHIVE_URL=...`.

The process logs a line like:

```text
[DedicatedServer] READY DUALMON_JOIN_CODE=ABC123 BUILD=cloud-authority-v5-20260722
```

Both players select **Join** in DualMon and enter that same code. Neither player
selects **Host** for a cloud-authoritative match.

Both clients must use multiplayer protocol v5 (`cloud-authority-v5-20260722`).
The server rejects older builds instead of allowing them to render incomplete
hands or disagree about Invoker identity.

This process makes outbound connections to Unity Authentication and Relay. It
does not require an inbound game port, so it can run as a persistent background
service on a container host such as Railway. Vercel serverless functions are not
appropriate because this Unity process must remain alive for the whole match.

The server also writes `dualmon-server-ready.json` under Unity's persistent data
directory for hosting automation to read.
