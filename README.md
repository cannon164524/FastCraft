# FastCraft

A small, fast launcher for **Minecraft: Java Edition** on Windows, written in C# / WPF on .NET 8 — and a faster way
to play the newest release. On **26.3**, FastCraft starts the **FastCraft edition**: the same Minecraft, booted
through Fabric with a curated set of performance mods, a speed preset and tuned JVM flags, instead of plain vanilla.
Older versions have no FastCraft edition, so choosing one warns you and launches vanilla Minecraft.

Online play needs a Microsoft account that actually **owns** Minecraft: Java Edition — the ownership check is part
of the sign-in, and FastCraft never fakes a session. For single player there is an **offline profile** (a local
nickname), which is what you use while Microsoft's sign-in is unavailable; it cannot join online servers or Realms.

## The FastCraft edition

The edition is **Mojang's client plus a performance layer**: FastCraft installs the Fabric loader and a curated set
of client-side performance mods into the game folder, writes a performance preset into a fresh game folder, and
starts the game through Fabric with tuned JVM flags. The game files are still official Mojang files, downloaded from
Mojang's CDN after the ownership check — a launcher cannot ship a different Minecraft, and it does not try to.

**Supported version: 26.3 only.** `FastEdition.GameVersion` in `Fast/FastEdition.cs` is the single place that
decides it; bumping that constant is what supporting a new release means. Selecting any other version shows a
warning on the Play view and launches vanilla Minecraft, without any of the performance layer.

What it installs, newest release build per project for that game version from Modrinth:

| Mod | What it speeds up |
| --- | --- |
| Sodium | the chunk renderer |
| Lithium | game logic and ticking |
| Krypton | the network stack |
| ImmediatelyFast | batched immediate rendering |
| EntityCulling | entities you cannot see |
| MoreCulling | hidden block faces |
| ModernFix | load time and memory |
| Fabric API | the interfaces the others are built on |

FerriteCore is deliberately not installed: its newest build is for an older game version and only carries a 26.3
tag, so it would risk a crash for a small memory gain. ModernFix has no 26.x build at all today, so it is skipped
by the rule below and joins automatically when it publishes one. MoreCulling's required Cloth Config is fetched with
it.

A mod with no build for the supported version is **skipped and logged**, never guessed at. Required dependencies of
whatever resolves are fetched with it. The launcher keeps a list of the jars it installed
(`mods\.fastcraft-managed.txt`): a mod that gets a new build has its old jar removed, and **files you added to
`mods` yourself are never touched**.

What to install is looked up once and then cached for twelve hours
(`%APPDATA%\FastCraft\cache\fast-edition-<version>-v2.txt`), so a launch behind a warm cache makes **no catalogue
requests at all** — it only checks the jars already on disk. The `v2` is bumped whenever the mod set changes, so a
new set is looked up instead of a stale one being reused. A lookup that found no mod is not cached, so a
Modrinth outage is retried on the next launch instead of being remembered for half a day.

These are client-side renderer and memory optimisations, not a cheat client — but individual servers set their own
rules about modified clients, so check theirs before joining one.

The preset is written only into a game folder that has no `options.txt` yet — 12 chunks of render distance, 10 of
simulation distance, fewer particles, no entity shadows, vsync off, 260 FPS cap. Change anything you like in the
game's video settings; FastCraft will not overwrite it.

**When it cannot be installed** — Fabric has no build for the version yet, Modrinth is unreachable, a download fails
— the game still starts, as plain vanilla; the window says so in a message and the log explains it in lines beginning
`[fast]`. Knowing that is better than a launch that dies or a silence that hides it.

The mods folder is reported in the log on every launch: `[fast] Mods folder: … (N jar(s) before this launch)`, then
`[fast] Mods folder now has M jar(s), K from the FastCraft set`, then one line per jar it installed. If the set is
not there, those lines say where it went — usually `Modrinth … has no 26.3 build yet; skipping it`, which is the
publisher telling us there is nothing to install yet, not a launcher bug.


## The look inside the game

26.3 is not only started faster, it looks like FastCraft. Two things do that, both generated on
the first launch of the edition and both reversible:

**A resource pack FastCraft draws itself** (`instances\26.3\resourcepacks\FastCraft.zip`). The
images are drawn by the launcher in its own palette, so the game and the launcher cannot drift
apart: the amber wordmark with the two speed chevrons, a "FASTCRAFT EDITION" line under it, dark
button plates that turn amber under the pointer (the main menu **and** the pause menu use them),
a near-black menu backdrop instead of the dirt, the title screen's panorama replaced by the
launcher's dark aurora, FastCraft splashes, and `menu.game` overridden so the pause menu header
reads **FastCraft · Game Menu**. The texture paths and the pack format are read from this
version's own asset index and `version.json`, so a path the game does not have is skipped and
named in the log instead of being guessed at.

- The title's two textures — the wordmark and the "edition" line — are drawn at **the exact pixel size
  the game ships** (274×44 and 110×16). The title screen blits them at that size instead of scaling
  them, so a texture with twice the pixels was read from its top-left corner and looked cut off; the
  mark also keeps a tenth of its height as a safe margin on every side. The button and backdrop sprites
  are nine-sliced, so those stay at twice the pixels.

**A FancyMenu set-up** (`instances\26.3\config\fancymenu\`). A resource pack cannot add text, so
FancyMenu does what a pack cannot: the custom pause menu title and a small `FASTCRAFT` mark in
the pause menu's corner, plus `modpack_mode = true` so its own editing UI stays out of a menu
that is supposed to look like a client. The screen class names are read from the client jar.

- **On by default for everyone.** Every launch of the edition puts `"file/FastCraft.zip"` into the
  `resourcePacks` line of `instances\26.3\options.txt` if it is missing, keeps every other pack and setting,
  removes it from `incompatibleResourcePacks`, and writes the `version` line the game needs — an options.txt
  without it is replaced with defaults, which silently threw the pack selection away. Switching it off by hand in
  Options therefore lasts until the next launch.
- The pack declares `min_format` / `max_format` as **integer arrays** — `[97, 0]` to `[97, 1]` for 26.3 — read from
  this version's own `version.json` (`pack_version.resource.major` / `.minor`, strings since 25w31a). A decimal is
  not a valid value, and a minimum below 65 without `pack_format` is rejected outright; either mistake makes the
  game label the pack **"broken or incompatible"**, which is what happened before. When the format cannot be read,
  the pack declares 65 and up instead of guessing a decimal.
- The override of `menu.game` is **merged into the version's own `en_us.json`**, taken from the
  client jar. A pack's lang file replaces the whole file, so shipping only that one key would have
  turned every other label in the interface into raw translation keys.
- `instances\26.3\.fastcraft-brand.txt` records what was applied. Delete it to have the next
  launch write the pack and the menu set-up again; `Branding.Version` in `Fast/Branding.cs`
  is what forces a rewrite when the art changes.
- The FancyMenu layout applies to the **English** UI, since a pack can only override the
  `en_us` translation; on another language the pause header keeps its normal text.
- A resource pack may not change the Mojang logo — the game blocks that on purpose — and none
  of this touches the HUD or the game's own screens beyond the menus above.

FancyMenu is a 38 MB download with three dependencies; it arrives with the other mods on the
first 26.3 launch of the edition.

### The amber tier

`fastcraft-amber.jar` is **FastCraft's own mod**, in `fastcraft-mod/`. The workflow builds it with Gradle
and embeds it in `FastCraft.exe`, so the launcher writes it into `mods` itself — there is no download of
its own, which is what lets it work while the repository is private. It adds a tier between copper and iron:

| Thing | Stats |
| --- | --- |
| `fastcraft:amber_ore` | 12 veins per chunk, y −16 to 48, stone or deepslate |
| `fastcraft:raw_amber` | drops from the ore, smelts or blasts into an ingot |
| `fastcraft:amber_ingot` | the repair item and the crafting material |
| `fastcraft:amber_sword` / `_pickaxe` / `_axe` / `_shovel` / `_hoe` | 220 uses, mining speed 5.5, 1.5 attack damage, level 12 |

Copper is 190 uses, speed 5, damage 1; iron is 250, speed 6, damage 2. The five tools are crafted in the
usual shapes and the ore is mined with a stone pickaxe or better.

Honest state: this is the first Java code in the project, so the 26.3 API and the Gradle/Loom version are
resolved by the workflow from Fabric's and Modrinth's live data instead of being hard-coded, and the mod build
step is allowed to fail without stopping the launcher release — the log names the call that moved. The ore's and
the tools' textures and names come from the resource pack in the build after the mod first compiles.



## The interface

One window, no tabs inside tabs: a 228 px rail with four views — **Play**, **Versions**, **Settings**, **Logs** —
the account card pinned to its bottom, and a status strip along the bottom edge that says what the launcher is
doing.

- **One accent, used as light.** Near-black translucent surfaces, hairline borders, one amber accent (`#D9A55B`,
  glow `#FFC077`) on the primary action, the selection marker and the icon; blurred halos sit behind buttons, the
  selected nav item, a focused field and the progress fill.
- **Ambient light.** Three soft orbs drift behind the pages at different speeds, and the cards are translucent so
  that light bleeds through them instead of sitting on top.
- **The version you are about to launch is the headline** of the Play view — 46 px in an amber gradient — and it
  swells and flares whenever the selection changes. Choosing a version happens in the Versions list.
- **Motion with weight:** a page change rises 30 px, swells slightly and sharpens out of a blur while its children
  follow in a staggered wave; buttons squash on press and release with an elastic overshoot; the launch button's
  halo breathes; the status dot pulses while work is running.
- **Its own window chrome** (`WindowChrome`, 46 px caption, drawn minimise/maximise/close), so the launcher does not
  wear Windows 10's title bar on Windows 11.

## The icon

The mark is two speed chevrons in a dark rounded tile. It is drawn once as a `DrawingImage` in `App.xaml` and reused
for the title bar, the watermark on the Play view and the window/taskbar icon (rasterised at runtime).

`tools/make-icon.ps1` draws the same shapes with GDI+ and writes `Assets\FastCraft.ico` in seven sizes (16–256,
PNG-compressed entries). The CI workflow and `build-exe.bat` run it before publishing, and `FastCraft.csproj` only
uses the file when it exists — a build without PowerShell still works, it just keeps the default exe icon.

## Why it is fast

- **Native .NET / WPF app.** No Electron, no embedded browser: the window is up in well under a second.
- **32 parallel HTTPS downloads** with keep-alive connections (`SocketsHttpHandler`), so the first install
  saturates your connection instead of downloading one file at a time.
- **Persistent SHA-1 verification cache** (`%APPDATA%\FastCraft\cache\verified.json`). Every downloaded file
  is hashed once, then only its size and last-write time are checked on later launches — those 3.000 asset
  files are no longer re-hashed every time you press Launch.
- **No re-downloading.** Files are skipped when the size matches, or when the hash matches after a single
  verification. Only changed files are fetched again.
- **Natives the way the official launcher handles them.** Since 1.19 the `*-natives-windows` jars stay on the
  classpath and LWJGL unpacks its DLLs from them at startup; only 1.18 and older have native archives that
  FastCraft unpacks itself, into a clean folder each launch, so a stale DLL cannot block the game.
- **Resolved version JSON is reused**; the version list is cached locally and refreshed in the background.
- **Reuses the official launcher's files.** If `%APPDATA%\.minecraft` already has the libraries and assets
  (because you played vanilla), FastCraft copies them instead of downloading them again; every copied file is
  then hash-verified, so a stale file is replaced. `.minecraft` itself is only read, never written.
- **Optional tuned G1 flags** for Java 17+ (Aikar-style flags), and the game process is started with **High**
  priority, which reduces stutter while other Windows apps are running.

Honest limits: the launcher side (checking, downloading, preparing files) is faster than the vanilla launcher, and
once the game starts the FastCraft edition's mods are what raise the frame rate — how much depends on your hardware,
render distance and world. A launcher cannot rewrite Minecraft's renderer, so what the edition does is add the mods
that do, keep them current, and start the game the way they expect. Minecraft's own startup (JVM, LWJGL natives,
roughly 100–200 MB of game and asset loading) is still there; FastCraft only avoids redundant work around it.

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

### If Minecraft crashes with `Failed to locate library: lwjgl.dll`

That was a real FastCraft bug, fixed after build `cae4525`. Since Minecraft 1.19 the natives travel as
`*-natives-windows` jars on the **classpath**, and LWJGL unpacks `lwjgl.dll` from them at startup. FastCraft used to
take those jars off the classpath and unpack them into `natives\` instead, a folder the game no longer reads, so the
first LWJGL call died. Update to the newest build; it also prints the classpath size and the number of unpacked
native archives to the window log, and an "already up to date" run re-checks every file it needs.

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
2. Pick a version. The list defaults to the newest release, which is the version the FastCraft edition is built on;
   picking anything older shows the warning that it will be vanilla Minecraft.
3. Press **Launch FastCraft** (or **Launch Minecraft** for an older version). On the first launch it downloads the
   version, libraries, natives and assets, then Fabric and the performance mods; after that, launching only checks
   what changed.
4. **Without signing in**: tick **Offline mode**, type a player name, and press Launch — see below.

If the game exits with an error, FastCraft names the newest **crash report** and the **game log**
(`instances\<version>\crash-reports` and `instances\<version>\logs\latest.log`) in a message and in the window's log,
and the window's log already holds the game's own output. Those files are what explain a crash.

### Offline mode

Sign-in can be unavailable for reasons that have nothing to do with the game itself, so FastCraft can start
Minecraft with a local profile instead:

- The name is turned into the same UUID Java gives an offline player (`UUID.nameUUIDFromBytes("OfflinePlayer:<name>")`,
  a version-3 UUID), so every offline launch with that name is the same player and keeps its single-player
  data. A signed-in account has its own Mojang UUID, so it is a different player in that same world; to keep
  the offline inventory, copy `playerdata\<offline-uuid>.dat` to `playerdata\<real-uuid>.dat` in the world folder.
- The session handed to the game is a placeholder and `--xuid` / `--clientId` are left out: nothing pretends to be a
  Microsoft session.
- **Single player and LAN only.** Online servers, Realms, skins and chat signing need a real session and refuse an
  offline player. That is not a limitation FastCraft can work around, and it does not try.
- Game files still come from Mojang's CDN. This mode does not license the game: it is for playing a copy you own
  when Microsoft's sign-in is unavailable.

### When sign-in fails

If signing in says *"Microsoft sign-in could not be started (HTTP 400)"*, Microsoft rejected FastCraft's app id
(the message now includes Microsoft's own error text; `AADSTS700016` means the id no longer exists). Register your
own Azure app — Microsoft Accounts tenant, public client, device code flow — and put its id in
`AuthService.ClientId` in `Auth/AuthService.cs`.

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

1. In the Azure portal, create an app registration in the **Microsoft Accounts** tenant, account type
   *personal Microsoft accounts only*, no redirect URI.
2. Under *Authentication*, turn on **Allow public client flows** (the device code flow needs it).
3. *API permissions* needs **nothing**. `XboxLive.signin` is a Microsoft Account scope that travels in the sign-in
   request itself, not an Azure permission you can grant — that is why "Xbox Live" is not listed under
   *APIs my organization uses*, and it does not have to be.
4. **Mojang has to approve the app for the Minecraft API**: submit it at https://aka.ms/mce-reviewappid. Until it
   is approved, `api.minecraftservices.com` refuses the profile call, so the built-in id stays the one that works.
5. Put the app (client) id into `%APPDATA%\FastCraft\client-id.txt` — the bare GUID, or the whole line pasted
   from the portal, since the first GUID in the file is used — and start FastCraft again. The first line of
   `auth.log` (`Client id: …`) names the one in use; delete the file to go back to the built-in id.

Every sign-in step is also written to `%APPDATA%\FastCraft\auth.log` with its HTTP status and the server's
error text (`Device code`, `Xbox Live`, `XSTS`, `Launcher login`, `Profile`, `login_with_xbox`, `Entitlements`),
never a token.
For refusals the response body and the headers that explain a rejection (`WWW-Authenticate`, `x-ms-error-code`,
`x-ms-request-id`, …) are included, and the same detail appears in the launcher's message.
That file is what to include in a bug report; the window title shows the version you are running.

## Updates

Every push to `main` builds FastCraft and publishes it as a GitHub release (`v1.0.<build number>`, with
`FastCraft.exe` attached). **The release notes are the commit subjects since the previous release**, so what
you read in the pop-up is what changed. An installed FastCraft checks that release on start; when it is newer,
a window pops up with the versions, those notes and an **Update now** button. If you are several builds behind,
every release between yours and the newest is listed, newest first, with its date. The new exe is downloaded to
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

- No Forge, NeoForge or Quilt, and no mod management of your own: Fabric comes with the FastCraft edition on 26.3,
  and mods you drop into a version's `mods` folder are left alone but not managed.
- No Minecraft Bedrock/Windows Edition.
- No multiple accounts, no per-instance mod management.
- The launcher itself is not code-signed, so Windows SmartScreen may warn on first run.
- Versions older than 1.6 use the retired legacy sign-in and will not start with a Microsoft
  account. 1.6 and newer work; the newest releases need Java 21, which FastCraft downloads for you.

## Legal

FastCraft is not affiliated with Mojang or Microsoft. It contains no game code: it downloads the official files
from Mojang's public CDN after you sign in with an account that owns the game, and it refuses to continue when the
account does not own Minecraft: Java Edition. Do not use it to play without a licence.

The FastCraft edition also downloads the Fabric loader from Fabric's maven and the performance mods from Modrinth.
Those are other people's work under their own licences, installed into your own game folder; FastCraft neither
re-hosts nor modifies them.

Ownership is confirmed with the account's Minecraft: Java Edition profile: `/minecraft/profile` answers 404 for an
account without the game, and that account is refused. `/entitlements/mcstore` is not needed for that, but FastCraft
asks it once after a failed sign-in, because its answer separates a token Minecraft refuses from a profile call it
refuses.

The offline profile is there so that an owner can play single player while Microsoft's sign-in is unavailable. It
verifies nothing and grants no online access, and it is not a licence: use it with a copy of the game you own.

The sign-in uses Prism Launcher's public Azure app id `c36a9fb6-4f2a-41ff-90bd-ae7cc92031eb` — a public client
in the Microsoft Accounts tenant with the device code flow enabled. The old Xbox Live id `00000000402b5328`
that many launchers used for years was retired and now answers `AADSTS700016`. If the current id is retired too,
register your own app in Azure (consumers tenant, public client, device code flow) and replace
`AuthService.ClientId` in `Auth/AuthService.cs`.
