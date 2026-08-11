# Windows UI polish plan

Tracking: #7

## Product shape

The management window becomes a focused two-view shell:

- **Games** contains a visually distinct **Action required** section above the configured **Games** section. Each section keeps its row actions directly beside its own list, and selection-dependent actions remain disabled until a row is selected.
- **Ignored** contains launcher-neutral ignored discoveries, including suppressed Steam games and foreground discoveries retained with enough metadata to restore them.

The window follows the Windows app-theme preference. Closing hides it to the tray; full exit and opening the authoritative iCloud folder live in the tray menu.

## Implementation checkpoints

1. Introduce theme resources, the new window shell, accessible status/error and empty states, badge variants, and the circular executable-path button.
2. Preserve ignored discovery metadata backward-compatibly and consolidate the Games/ignored navigation.
3. Update tray commands and replace the generic icon with a reusable application/attention icon.
4. Add regression coverage, measure the existing polling loop, update documentation, and complete Windows manual validation.

## Boundaries

This work does not change reminder files or completion semantics, does not add a reminder database, and does not add installers or launcher providers.
