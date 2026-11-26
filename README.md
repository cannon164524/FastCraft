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
   account that owns Minecraft. FastCraft checks ownership and loads your Minecraft name and UUID.
2. Pick a version (tick **Show snapshots** for snapshots).
3. Press **Launch Minecraft**. On the first launch it downloads the version, libraries, natives and assets; after
   that, launching only checks what changed.

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
- No self-updater, no multiple accounts, no per-instance mod management.
- The launcher itself is not code-signed, so Windows SmartScreen may warn on first run.
- Versions older than 1.6 use the retired legacy sign-in and will not start with a Microsoft
  account. 1.6 and newer work; the newest releases need Java 21, which FastCraft downloads for you.

## Legal

FastCraft is not affiliated with Mojang or Microsoft. It contains no game code: it downloads the official files
from Mojang's public CDN after you sign in with an account that owns the game, and it refuses to continue when the
account does not own Minecraft: Java Edition. Do not use it to play without a licence.

The sign-in uses the public Xbox Live client id `00000000402b5328`, which several open-source launchers use.
If Microsoft ever retires it, register your own app in Azure (public client, device code flow enabled) and replace
`AuthService.ClientId` in `Auth/AuthService.cs`.
