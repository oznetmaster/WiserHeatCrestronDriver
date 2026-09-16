# Coverage source review - 16 September 2026

The draft coverage snapshot now reflects the tracked runtime sources through `e24b5d40f41829a989279429fd207d588957902e`. This is a source review, not policy approval or completed test evidence. Generated local Debug revisions were excluded; the tracked manifest still declares release version `1.3.007.0000`. A final candidate version or any further behavior change requires another review.

Two source changes were reviewed against the prior snapshot:

| Source | Change | Coverage consequence |
| --- | --- | --- |
| `WiserRoomEntity.cs` | Publish inline schedule choices and refresh the property definition when available values change. | Retain the complete initial choice/selection checks and add a distinct live-definition check: updated labels/options must reach an already-open selector, selection must remain associated with the correct ID, and removed choices must not assign an unintended schedule. Use isolated schedules and restore every affected assignment. |
| `IncludeInPkg/Translations/en-US.json` | Shorten the hot-water actions to Turn On/Turn Off and the shared schedule action to Save All. | Check actual rendered labels, readability and action scope. Save All still affects all days in the selected shared schedule; its shorter caption does not narrow the restoration obligation. |

The other nine declared sources, including both UI definitions and the tracked manifest, still match the reviewed snapshot. The manifest already includes the approved GitHub support website and empty email field; no metadata pin was changed. Local Debug-version edits were not imported. Check that support route again in the final package/help.

The added Android control fixture does not modify production behavior. Its assembly, dependencies, discovery inventory and assertions must be bound separately as a test producer; a runtime source hash does not identify the test executable.

Reading current lists and cancelling the editor does not prove live definition refresh, selection actions, shared saves or physical behavior. The new control cycle likewise does not satisfy the complete official control requirements by itself. All generated producers remain unbound and the execution contract remains `submissionReady: false` until actual candidate-specific validation and policy review are complete.
