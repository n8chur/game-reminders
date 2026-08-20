# Game Reminders

Game Reminders saves a reminder on Windows or by voice on iPhone, synchronizes it through iCloud Drive, and shows it the next time the associated game launches. There is no Game Reminders server or account: files in iCloud Drive are the authoritative reminder store.

## Requirements

- Windows 10 or Windows 11 on an x64 PC
- iCloud for Windows with iCloud Drive enabled
- An iPhone or Mac with Apple Shortcuts for the optional voice workflow

The Windows download is self-contained; installing the .NET SDK or runtime is not required. Game Reminders does not require administrator access, inject into games, or interact with anti-cheat software.

## Supported features and limitations

Game Reminders supports multiple pending and completed reminders, in-app reminder creation, a cross-device Apple Shortcut, manual executable mapping, automatic Steam discovery, conservative foreground-game discovery, multiple executable paths per game, per-user launch at login, light and dark Windows themes, and notification-area operation.

Version 0.0.2 supports Windows x64 only. Steam is the only launcher with automatic library discovery; other games require manual mapping or review of a foreground discovery. iCloud Drive is required for synchronization, and the app does not provide remote accounts, shared lists, automatic updates, telemetry, or crash reporting. The Windows packages are unsigned.

## Install on Windows

Download one of these files from the latest [GitHub Release](https://github.com/n8chur/game-reminders/releases):

- `GameReminders-0.0.2-win-x64-setup.exe` is the recommended installer. It installs for the current user, adds a Start Menu entry and uninstaller, enables launch at sign-in by default, and can optionally create a desktop shortcut.
- `GameReminders-0.0.2-win-x64-portable.zip` contains the same application without an installer. Extract the complete ZIP to a permanent folder before running `GameReminders.exe`.
- `Game-Reminder.shortcut` is the validated Apple Shortcut for creating reminders from iPhone or Mac.

Version 0.0.2 is not code-signed. Windows may show a Microsoft Defender SmartScreen warning because it cannot verify the publisher. Confirm that the file came from this repository's Releases page before choosing **More info** and **Run anyway**. Do not bypass a warning for a copy from another source.

### First launch

1. Install iCloud for Windows, sign in, and enable iCloud Drive.
2. Start Game Reminders.
3. Select the `Shortcuts` folder inside iCloud Drive. When one standard location is unambiguous, the app selects it automatically.
4. Game Reminders creates or opens the fixed `Game Reminders` child folder and verifies that it is usable.
5. Allow the app to request **Always keep on this device** for that child folder. If iCloud rejects the request, apply that option to the folder in File Explorer and retry.
6. Confirm **Launch Game Reminders when I sign in to Windows**. Installer users begin with this enabled unless they cleared the installer option; portable users can opt in here.
7. Add a game manually or choose **Scan Steam**.

The resulting authoritative folder is:

```text
iCloud Drive/
└── Shortcuts/
    └── Game Reminders/
        ├── games.json
        ├── inbox/
        ├── completed/
        └── invalid/
```

Do not create a second store or merge two copies of this directory. If the folder is moved or unavailable, startup presents a recovery screen and keeps the previous setting until you deliberately confirm a valid replacement.

## Install the Apple Shortcut

Download and import [`shortcuts/Game Reminder.shortcut`](shortcuts/Game%20Reminder.shortcut). The Shortcut uses the built-in iCloud Shortcuts container and the fixed `Game Reminders` child, so it requires no device-specific folder selection. Let it synchronize through iCloud to the iPhone or Mac where you want to use it.

Run **Game Reminder**, select a registered game, and dictate or type the reminder. The Shortcut reads the Windows-managed catalog and creates a new immutable reminder file; it never edits `games.json`. For the auditable workflow definition, artifact validation, and Apple signing details, see [`shortcuts/README.md`](shortcuts/README.md).

## Manage games

Use the **Games** view to add, search, edit, or remove game mappings.

- **Scan Steam** discovers installed Steam games. A confident executable match is added automatically.
- **NEW** identifies a discovery that has not yet been reviewed.
- **INSTALLING** means Steam is still downloading the game, so it has no executables yet. The entry resolves itself within about 30 seconds of the install completing; no rescan is needed. If you cancel the download, the entry is dropped automatically rather than left behind, and reinstalling adds it back.

- **NOT INSTALLED** means Steam no longer reports the game. Its executable paths and reminders are kept and start working again when you reinstall it. Use **Hide uninstalled** above the games list to keep these out of the way. You do not need to remove such a game; **Remove** is for games you never want scans to add back.

- **ACTION REQUIRED** means executable selection was ambiguous. Open the game, select or enter the correct executable path, and save it before relying on launch detection.
- Manual games can use **Browse** to select one or more `.exe` files.
- Removed Steam games and ignored foreground discoveries appear under **Ignored** and can be restored.

Steam entries store paths relative to `steamapps\common` when possible. Other mappings use normalized executable identities. Games are resolved by stable ID rather than display name, so renaming a game does not disconnect existing reminders.

## Use reminders

The **Reminders** view is the default screen.

- **New reminder** creates a pending reminder for a configured game without using the Apple Shortcut.
- **Pending** lists reminders waiting for their game to launch.
- **Show on next launch** closes a popup but leaves its reminder pending. During the current app session it remains visible as deferred in the management UI; restarting the app clears only that in-memory distinction.
- **Dismiss** in a launch popup, or explicit completion in the management view, moves a reminder to **Completed**.
- A completed reminder can be marked pending again or deleted individually. **Clear completed** permanently removes the completed archive after confirmation.

Closing a popup, closing the main window, shutting down Windows, or an application crash never completes a reminder. A reminder is completed only after an explicit action successfully archives its file.

## Notification area and startup

Closing the main window leaves Game Reminders running in the Windows notification area so it can detect game launches. The tray menu can reopen the window, open the iCloud folder, scan Steam, or fully exit. Use **Launch at login** in the main window to change the current user's startup registration; it does not require administrator privileges.

The client scans for reminders on startup, after filesystem changes, before showing a popup, and every 60 seconds as a fallback. Files should be available offline because the complete `Game Reminders` folder is pinned with **Always keep on this device**.

## Upgrade, portable use, and uninstall

To upgrade an installed copy, run the newer installer. Its stable application identity replaces the installed binaries while preserving local settings, startup preference, and all iCloud files. Exit Game Reminders first if practical; setup detects a running copy and requests that it close before files are replaced.

Portable and installed copies use the same per-user settings at `%LocalAppData%\GameReminders\settings.json`. To switch between them, fully exit the running copy and start the other one. Never run both copies at once; single-instance protection permits only one active Game Reminders process.

Uninstall from **Settings > Apps > Installed apps**. Uninstall removes the installed program, shortcuts, and Game Reminders launch-at-login entry. It intentionally preserves `%LocalAppData%\GameReminders` and the complete iCloud `Game Reminders` folder. Delete those manually only if you intend to discard the configuration or reminder history.

## Data and privacy

- `games.json`, pending reminders, completed reminders, and quarantined malformed reminder files live in the selected iCloud folder.
- Windows-only settings and discovery-review state live under `%LocalAppData%\GameReminders`.
- Reminder files include their message text because that is the data synchronized between the Shortcut and Windows.
- Game Reminders has no telemetry, cloud service, crash reporter, or automatic updater.

Malformed reminder files are preserved and moved to `invalid` only after repeated failures. Archive collisions and ambiguous filesystem states fail visibly rather than overwriting or deleting data.

## Troubleshooting

### The app cannot find or use iCloud

Confirm that iCloud Drive is enabled and fully signed in, then locate `iCloud Drive\Shortcuts` in File Explorer. Choose that enclosing `Shortcuts` folder during recovery—not a second or copied `Game Reminders` folder. Mark its `Game Reminders` child **Always keep on this device**.

### A reminder does not appear

Confirm the reminder file has synchronized into `inbox`, the game is configured, and its executable path is correct. Resolve any **ACTION REQUIRED** badge. Keep Game Reminders running in the notification area and test by fully exiting and relaunching the game. The periodic scan can take up to 60 seconds after an iCloud change.

### The Shortcut cannot read the catalog

Open the Windows app and confirm it is pointed at `iCloud Drive/Shortcuts/Game Reminders`. Verify that `games.json` exists and has synchronized to the Apple device. Reimport the repository-hosted Shortcut if its file actions were edited.

### A reminder keeps returning

**Show on next launch** intentionally leaves it pending. Use **Dismiss** or complete it explicitly in the management view. If archival fails, the app reports the error and preserves the pending reminder.

### Windows blocks the download

Version 0.0.2 is unsigned. Download it only from this repository's Releases page, inspect the warning, and use **More info > Run anyway** if the source and filename are correct.

### Settings are malformed or the store is unavailable

Follow the error shown by the app. It preserves malformed settings and reminder files rather than silently resetting them. Keep a backup before manually correcting JSON. Do not merge reminder directory trees.

## Development

The Windows client targets .NET 10 and WPF. On Windows with the .NET 10 SDK installed:

```powershell
dotnet restore GameReminders.slnx
dotnet test GameReminders.slnx --configuration Release --no-restore
dotnet build GameReminders.slnx --configuration Release --no-restore
```

Pull requests build and validate the app, Shortcut, portable ZIP, and Inno Setup installer. Pushing a tag that exactly matches the centralized version—for example `v0.0.2`—publishes both Windows packages and the validated Apple Shortcut to a GitHub Release.

Release operators should complete [`docs/RELEASE_CHECKLIST.md`](docs/RELEASE_CHECKLIST.md) before publishing the version tag.
