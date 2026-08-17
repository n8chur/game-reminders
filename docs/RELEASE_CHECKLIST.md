# Windows release checklist

Use this checklist on Windows x64 before publishing a Game Reminders version tag. Test with disposable data; never point destructive test cases at the only copy of a real reminder store.

## Automated packages

1. Open the successful pull-request workflow run.
2. Confirm Shortcut validation, restore, tests, publish, version validation, ZIP inspection, and Inno Setup compilation all passed.
3. Download the workflow artifact and confirm it contains exactly the expected versioned installer and portable ZIP.
4. Extract the ZIP and confirm `GameReminders.exe` is at its root and starts without a separately installed .NET runtime.

## Clean install

1. Use a Windows user profile without Game Reminders settings or startup registration.
2. Run the installer without elevation and confirm its destination is `%LocalAppData%\Programs\Game Reminders`.
3. Confirm the Start Menu entry, Add/Remove Programs entry, checked-by-default launch-at-sign-in option, and optional desktop shortcut.
4. Launch the app and complete setup against a disposable `iCloud Drive\Shortcuts\Game Reminders` store.
5. Confirm the child folder is pinned or that the manual **Always keep on this device** instructions appear.
6. Confirm first-run setup reflects the installer option's actual launch-at-login state, then confirm the app starts hidden after signing out and back in.
7. Import the repository Shortcut on a clean Apple device, create a reminder, wait for iCloud synchronization, and confirm launching the mapped game displays it.

## Upgrade and data preservation

1. Create pending and completed reminders, an ignored discovery, and a game mapping; keep launch at login enabled.
2. Run the installer again while Game Reminders is in the notification area and confirm setup requires the running copy to close before replacement.
3. Launch the upgraded copy and confirm settings, discovery state, catalog, pending reminders, completed reminders, and startup registration remain intact.
4. Confirm **Show on next launch** still leaves a reminder pending and **Dismiss** archives it only after the explicit action succeeds.
5. Disable launch at login in the app, run an upgrade, and confirm setup initializes the task from the actual disabled registry state and does not re-enable startup.
6. Re-enable launch at login in the app, run another upgrade, and confirm setup initializes the task checked and preserves startup.

## Portable and uninstall

1. Fully exit the installed copy, extract the portable ZIP to another folder, and run it.
2. Confirm it reads the same per-user settings and store without creating a duplicate reminder database.
3. Exit the portable copy and restart the installed copy from the Start Menu.
4. Start uninstall while the installed copy is in the notification area; close the app when prompted and complete uninstall.
5. Confirm installed binaries, shortcuts, the Add/Remove Programs entry, and the `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\GameReminders` value are gone.
6. Confirm `%LocalAppData%\GameReminders` and the complete iCloud store remain unchanged.

## Release

1. Confirm `Directory.Build.props` contains the intended semantic version and the release tag will be exactly `v<version>`.
2. Confirm the README filenames and unsigned-package warning match the release.
3. Merge the release PR only after its Windows workflow succeeds.
4. Push the matching version tag and confirm the tag workflow publishes one GitHub Release containing both packages.
5. Download both assets from the Release page and repeat the executable-name and launch checks on the exact published files.
