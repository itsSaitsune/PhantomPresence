# Phantom Presence

Discord Rich Presence for **Persona 5 Royal** on Windows.

Shows the in-game date, time, and detected activity or location. Includes a tray icon, a game launcher shortcut, and automatic exit when the game closes. The shared Discord application and artwork are preconfigured; no bot or account token is needed.

## Compatibility

- **Xbox / Game Pass PC:** tested game layout from package **1.10.27.0**.
- **Steam / modified builds:** optional, experimental, and not live-tested. Limited to date/time and sequence-based menu, battle, story, and some activity detection. Location names and conversation partners are not implemented for this reader. Other executable layouts may be rejected.

## Project files

- `scripts/` — C# source.
- `resources/` — artwork, font, default configuration, location mappings, and third-party notices.
- `P5RPresence.csproj` — project file.

## Download

Download **P5XboxPresence.exe** from [Release v1.1](https://github.com/itsSaitsune/PhantomPresence/releases/tag/v1.1). It is one Windows x64 executable with .NET and all assets included. Run it from a writable folder. Config, logs, and an optional game shortcut are created beside it when used; bundled files extract automatically to .NET's cache.

## Build

Requires Windows x64 and the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```powershell
dotnet publish P5RPresence.csproj -c Release -r win-x64 --self-contained true -o publish
```

Open `publish/P5XboxPresence.exe`. The publish folder contains one executable, including the runtime, assets, font, location mappings, and license notices.

## Use

1. Open Discord desktop.
2. Open Phantom Presence and select `P5R.exe` using **Browse**, or paste its full path.
3. Click **Connect Now** to launch the game and connect presence.

For an unverified Steam/modified build, enable **Experimental Steam / modified builds** before saving.

**Allow autostart → Save Config** creates `Play Persona 5 Royal.lnk` beside the app. Use that shortcut to start both together. It does not start with Windows, and launching the game independently cannot start a fully closed presence app.

After setup, the app hides to the tray. Double-click the tray icon to reopen it. Configuration is stored in `config.json`; diagnostics are written beside the app. The reader only requests read/query access to the game.

## Credits

Application source is licensed under [GPL-3.0](LICENSE). Game-state references include [p5rpc.lib](https://github.com/AnimatedSwine37/p5rpc.lib), [MultiPresence](https://github.com/Dekirai/MultiPresence) ([MIT notice](resources/licenses/MultiPresence.txt)), and location/activity references from [Persona 5 Royal Mod Menu](https://github.com/Amicitia/Persona-5-Royal-Mod-Menu).

Exposé Regular by James Lewis: [font notice](resources/Fonts/NOTICE.txt). Persona artwork belongs to its respective owners; it is separate from the source-code license. This is an unofficial fan project.
