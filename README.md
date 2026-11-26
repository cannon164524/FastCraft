# FastCraft

A small, fast launcher for **Minecraft: Java Edition** on Windows, written in C# / WPF on .NET 8.

FastCraft only launches official Mojang versions (releases, and snapshots if you tick the box), and it only
works with a Microsoft account that actually **owns** Minecraft: Java Edition. There is no offline mode and no
piracy support — the ownership check is part of the sign-in.

## Why it is fast

- **Native .NET / WPF app.** No Electron, no embedded browser: the window is up in well under a second.
- **32 parallel HTTPS downloads** with keep-alive connections (`SocketsHttpHandler`), so the first install
  saturates your connection instead of downloading one file at a time.
- **Persistent SHA-1 verification cache** (`%APPDATA%\FastCraft\cache\verified.json`). Every downloaded file
  is hashed once, then only its size and last-write time are checked on later launches — those 3.000 asset
  files are no longer re-hashed every time you press Launch.
- **No re-downloading.** Files are skipped when the size matches, or when the hash matches after a single
  verification. Only changed files are fetched again.
- **Natives are extracted to a clean folder** each launch, so a stale DLL can never block the game.
- **Resolved version JSON is reused**; the version list is cached locally and refreshed in the background.
- **Reuses the official launcher's files.** If `%APPDATA%\.minecraft` already has the libraries and assets
  (because you played vanilla), FastCraft copies them instead of downloading them again; every copied file is
  then hash-verified, so a stale file is replaced. `.minecraft` itself is only read, never written.
- **Optional tuned G1 flags** for Java 17+ (Aikar-style flags), and the game process is started with **High**
  priority, which reduces stutter while other Windows apps are running.

Honest limits: once Minecraft itself starts, the JVM, the LWJGL natives, and roughly 100–200 MB of game and
asset loading dominate startup. FastCraft makes the launcher side (checking, downloading and preparing files)
much faster than the vanilla launcher, and it avoids redundant work on every launch — it cannot make Minecraft's
own bootstrap instant, and it does not raise your in-game FPS.

## Get FastCraft.exe

The launcher is a Windows program, so it has to be built once (there is no ready-made exe in this repository).
Pick one:

1. **On a Windows PC: double-click `build-exe.bat`.** It needs the free .NET 8 SDK
   (https://dotnet.microsoft.com/download/dotnet/8.0) and writes `dist\FastCraft.exe`.
2. **Without a Windows PC: let GitHub build it.** Open the repository's **Actions** tab →
   *Build Windows exe* → *Run workflow*; when it is green, download the **FastCraft-windows-x64**
   artifact, unzip it and take `FastCraft.exe`.

`dist\FastCraft.exe` is self-contained: copy it anywhere, double-click it, and it runs without .NET installed.
`build-exe.bat` and the Actions workflow run the same command.

## If nothing happens when you double-click FastCraft.exe

- Use the exe from the **latest** build. Earlier builds uploaded only `FastCraft.exe` while the WPF
  runtime DLLs were left beside it, so the exe could not start. Now everything is bundled into the one file.
- FastCraft writes the reason for a failed start to `%APPDATA%\FastCraft\startup.log` and also shows it
  in a message box. If that log does not exist at all, Windows stopped the exe before it ran (see below).
- To see the exit code: open PowerShell in the exe's folder and run `& .\FastCraft.exe; $LASTEXITCODE`.
- An unsigned exe downloaded from the internet can be blocked by Windows: right-click the exe →
  *Properties* → tick *Unblock*, or in the SmartScreen prompt choose *More info* → *Run anyway*.

## Requirements

- Windows 10 or 11, 64-bit.
- .NET 8 SDK to build (the published executable is self-contained).
- A Microsoft account that owns Minecraft: Java Edition.
- Internet access on first launch: game files come from Mojang's CDN, not from this repository.

## Build

```bat
dotnet build -c Release
dotnet run -c Release
```

Publish a single self-contained executable:

```bat
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:ReadyToRun=true
```

The result is in `bin\Release\net8.0-windows\win-x64\publish\FastCraft.exe`.

## Using it

1. Press **Sign in with Microsoft**. A browser opens; enter the code shown in the window and sign in with the
   account that owns Minecraft. FastCraft checks ownership and loads your Minecraft name and UUID. Later starts
   reuse the saved session instead of asking again.
2. Pick a version (tick **Show snapshots** for snapshots).
3. Press **Launch Minecraft**. On the first launch it downloads the version, libraries, natives and assets; after
   that, launching only checks what changed.

If signing in says *"Microsoft sign-in could not be started (HTTP 400)"*, Microsoft rejected FastCraft's app id
(the message now includes Microsoft's own error text; `AADSTS700016` means the id no longer exists). Register your
own Azure app — Microsoft Accounts tenant, public client, device code flow — and put its id in
`AuthService.ClientId` in `Auth/AuthService.cs`.

### When sign-in fails

Mojang rate-limits the Minecraft sign-in endpoints hard, so **every attempt costs requests**, and repeated tries
push the wait out. FastCraft therefore keeps each run as small as it can:

- **A saved session is reused.** The stored Minecraft token is checked against `/minecraft/profile` first and only
  refreshed when that is refused, so a normal start costs one request instead of a whole Microsoft sign-in.
- **One token request per route.** `/launcher/login` — the endpoint Mojang's own launcher and Prism use — is asked
  once, the profile is then asked for once more if the first answer is a refusal (a just-issued token is sometimes
  refused once while it propagates), and the older `login_with_xbox` endpoint only runs as a fallback.
- **HTTP 429 stops everything.** When any answer is 429, FastCraft records when the wait ends in
  `%APPDATA%\FastCraft\cache\auth-throttle.txt` and refuses to send another request until then, naming the
  remaining minutes in the message.

So if it says *"Mojang is throttling sign-in"*, wait the minutes it names — about half an hour after several failed
attempts — then press Sign in with Microsoft **once**. A plain `HTTP 401` from `/minecraft/profile` with an empty
body is what Mojang returns while it throttles; it does not mean the account is wrong.

If Microsoft accepted the sign-in, Mojang handed out a Minecraft token, and the profile is still refused, two
things are left: too many attempts from your connection (only waiting a few hours helps, and a VPN or a shared
network makes that worse), or the request budget of the shared app id being used up by other people. The second
one you can rule out yourself:

1. In the Azure portal, create an app registration with the **Microsoft Accounts** tenant, account type
   *personal Microsoft accounts only*, no redirect URI.
2. Under *Authentication*, turn on **Allow public client flows** (the device code flow needs it).
3. Put the app (client) id — one GUID, nothing else — into `%APPDATA%\FastCraft\client-id.txt` and start
   FastCraft again. That id is then used for the device code and the refresh instead of the shared one, and
   `auth.log` names which one was used.

Every sign-in step is also written to `%APPDATA%\FastCraft\auth.log` with its HTTP status and the server's
error text (`Device code`, `Xbox Live`, `XSTS`, `Launcher login`, `Profile`, `login_with_xbox`), never a token.
For refusals the response body and the headers that explain a rejection (`WWW-Authenticate`, `x-ms-error-code`,
`x-ms-request-id`, …) are included, and the same detail appears in the launcher's message.
That file is what to include in a bug report; the window title shows the version you are running.

## Updates

Every push to `main` builds FastCraft and publishes it as a GitHub release (`v1.0.<build number>`, with
`FastCraft.exe` attached). An installed FastCraft checks that release on start; when it is newer, a window
pops up with the versions, the release notes and an **Update now** button. The new exe is downloaded to
`%APPDATA%\FastCraft\update\FastCraft.exe`, FastCraft restarts, and a small helper script replaces the old
exe with it.

- This works from the first build that contains the updater onwards; an older exe without it has to be
  replaced by hand once.
- **The repository has to be public**: GitHub only serves releases anonymously for public repositories. If the
  log says *"GitHub has no release data"*, the repository is private — make it public in
  Settings → General → Danger zone, and the popup starts working on the next start.
- If the exe cannot be replaced (for example because it sits in `Program Files`), the next start shows a
  message with the folder where the downloaded update is.
- *Check for updates on startup* can be turned off in the settings, and **Check now** runs the check on
  demand and shows the result.
- Local builds report version `1.0.0`, so they will offer every published release.

## Where things are stored

Everything lives under `%APPDATA%\FastCraft`:

| Folder | Contents |
| --- | --- |
| `versions\<id>\` | version JSON, client jar, `natives\` |
| `libraries\` | library jars, shared with the layout Mojang uses |
| `assets\` | asset index, objects, log configs |
| `runtime\` | Java runtimes downloaded from Mojang, when needed |
| `instances\<id>\` | per-version game directory (options, saves, mods) |
| `cache\` | version list cache and the SHA-1 verification cache |
| `settings.json` | memory, Java path, extra JVM args |
| `account.dat` | your session, encrypted with Windows DPAPI |

Your existing `.minecraft` folder is only read from, to avoid re-downloading files you already have.

## What it does not do (yet)

- No Forge, Fabric, NeoForge or Quilt mod loaders — vanilla versions only.
- No Minecraft Bedrock/Windows Edition.
- No multiple accounts, no per-instance mod management.
- The launcher itself is not code-signed, so Windows SmartScreen may warn on first run.
- Versions older than 1.6 use the retired legacy sign-in and will not start with a Microsoft
  account. 1.6 and newer work; the newest releases need Java 21, which FastCraft downloads for you.

## Legal

FastCraft is not affiliated with Mojang or Microsoft. It contains no game code: it downloads the official files
from Mojang's public CDN after you sign in with an account that owns the game, and it refuses to continue when the
account does not own Minecraft: Java Edition. Do not use it to play without a licence.

Ownership is confirmed with the account's Minecraft: Java Edition profile: `/minecraft/profile` answers 404 for an
account without the game, and that account is refused. The older `/entitlements/mcstore` check is not used — it now
answers HTTP 401 even for accounts that own the game.

The sign-in uses Prism Launcher's public Azure app id `c36a9fb6-4f2a-41ff-90bd-ae7cc92031eb` — a public client
in the Microsoft Accounts tenant with the device code flow enabled. The old Xbox Live id `00000000402b5328`
that many launchers used for years was retired and now answers `AADSTS700016`. If the current id is retired too,
register your own app in Azure (consumers tenant, public client, device code flow) and replace
`AuthService.ClientId` in `Auth/AuthService.cs`.
