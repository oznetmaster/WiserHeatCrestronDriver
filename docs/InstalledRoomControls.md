# Testing an installed room through Crestron Home

The optional `WiserHeatCrestronDriver.ControlProbe` console project reads the Wiser hub independently of the Home driver. It never sends a control command. Build it with .NET 10; it is development tooling, not part of the processor package or a separate NuGet package.

Room entities expose `controlDeviceId` (`normalized-hub-address/room/id`) and `controlStatus` (an epoch, completed count and pending count). A completion is published only after the command and its fresh hub-state read finish. Overlapping room commands, including schedule selection, are rejected while an action is pending.

The first supported probe observes the existing `enableSchedule` and `disableSchedule` commands. It requires a selected room initially following an assigned schedule in Auto mode, without an override or boost. An explicit existing manual setpoint must be between 5°C and 30°C. It may be below, equal to or above the scheduled target: the two targets are independent settings. The test temporarily selects Manual mode, observes the original manual target, then returns to Auto and verifies the current scheduled target. It preserves both stored values, the schedule assignment and guarded room settings. It does not test temperature setters or edit schedules. Selecting the live control test authorizes a brief real mode and potentially heating-demand change.

Rooms without `ManualSetPoint` are refused before control: the development hub created that field on its first Manual transition, and returning to Auto did not remove it. The initial hardware test caught this and remained failed. Setting the field to null was ignored, and field deletion was rejected. Do not weaken restoration checks to treat absent and newly created settings as equivalent.

Use your private `LiveTestSettings.json` with `hubHost`, `secret` and `controlRoomName`. First capture a fresh baseline:

```text
dotnet WiserHeatCrestronDriver.ControlProbe.dll capture --settings C:/Private/LiveTestSettings.json --baseline C:/Private/run/room-baseline.json
```

Capture refuses to overwrite an existing baseline. Use a new private path for every run; a baseline expires after one hour. The baseline contains private room state and must not be committed or uploaded as a public artifact.

In a private NUnit workflow `deployedControls` entry, select the exact installed room child and its actual root. Use `booleanCommands` with `true: enableSchedule` and `false: disableSchedule`, `stateProperty: scheduleEnabled`, and `invertBoolean: true`. Configure the probe arguments as the DLL path followed by `--settings`, the private settings path, `--baseline`, and the fresh baseline path. The backend supplies its request and response paths. The physical identity must exactly match the selected room's `controlDeviceId`.

The workflow's processor reservation covers commands and restoration. A missing response, unfinished command, changed room identity, changed guarded settings or unverified restoration retains recovery evidence and prevents a successful deployment gate. Do not clear a reservation without checking that the original schedule control has been restored. A returned temperature alone is insufficient evidence of restoration.

Automated coverage includes busy-room rejection, command completion after fresh readback, failure recovery, schedule boundaries, identity changes and override/settings refusal. Hardware validation on the development MC4-R passed Auto → Manual → Auto through an existing installed room child, with independent hub readings and unchanged guarded settings. The hub reports `FromManualMode` during Manual operation; the probe requires that origin and the unchanged manual setpoint. The existing room tile was retained and the reservation released.

Use the existing installed room child. Commissioning the platform's room creates a tile for that real room, not an independent test thermostat. Do not treat an existing user tile as a disposable test host. Private `.room.json` observation files are retained beside each probe response, including rejected observations; exclude them from published artifacts along with the baseline and credentials.
