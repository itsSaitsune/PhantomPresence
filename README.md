# Phantom Presence

Discord Rich Presence for **Persona 5 Royal** on Windows. Shows the in-game date, time, and activity or location.

## Download

Download **[PhantomPresence.exe — Release v1.2](https://github.com/itsSaitsune/PhantomPresence/releases/tag/v1.2)**. It is one Windows x64 EXE with .NET and assets included.

1. Open Discord desktop.
2. Run PhantomPresence.exe and select your P5R.exe.
3. Click **Connect Now**.

Quit an older copy before replacing the EXE. Existing settings are migrated automatically.

**Allow autostart → Save Settings** creates a shortcut that launches the game and presence together. The app closes when the game exits. It does not start with Windows. Double-click the tray icon to reopen settings.

Settings are stored in the Windows user registry. No config, status, or log files are created beside the EXE. The optional shortcut and .NET's temporary extraction cache are still used.

## v1.2 bugfixes

- Fixed confidant hangouts not being shown correctly.
- Fixed ordinary NPC conversations incorrectly showing as hangouts; they now show the location only.
- Fixed classroom, exam, and exam-result activity detection.
- Fixed missing and incorrect location labels, including the clinic and Big Bang Burger.
- Fixed burger-challenge text across all challenge levels.
- Fixed unwanted config, status, and log files being created beside the app.

## Compatibility

- **Xbox / Game Pass PC:** tested layout from package **1.10.27.0**.
- **Steam / modified builds:** optional and experimental; not live-tested. Limited date/time and sequence detection. Dialogue-based detection and general location names are not supported by that reader.

Confidant and location mappings use game-script references; not every scene/location has been live-tested. Normal story scenes remain generic. The shared Discord application/artwork are preconfigured; no bot or account token is needed.

## Build

Requires Windows x64 and the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```powershell
dotnet publish P5RPresence.csproj -c Release -r win-x64 --self-contained true -o publish
```

Run `publish/PhantomPresence.exe`. Source is in `scripts/`; assets and mappings are in `resources/`.

## Credits

[GPL-3.0](LICENSE). References: [p5rpc.lib](https://github.com/AnimatedSwine37/p5rpc.lib), [MultiPresence](https://github.com/Dekirai/MultiPresence) ([MIT notice](resources/licenses/MultiPresence.txt)), [field mappings](https://github.com/ShrineFox/Persona-5-Mod-Menu), and [game-script references](https://github.com/GuhLucena/p5r).

Exposé Regular by James Lewis: [font notice](resources/Fonts/NOTICE.txt). Persona artwork is separate from the source-code license. Unofficial fan project.
