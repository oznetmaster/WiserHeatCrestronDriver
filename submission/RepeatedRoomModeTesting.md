# Repeated room mode testing

`RoomScheduleControlRepeatedCyclesRestoreOriginalPolicy` is an opt-in Android `LiveControl` fixture. Select it explicitly and enable `AllowRepeatedRoomModeControl` in the private UI settings. It uses the ordinary `Rooms`, `ControlRooms` and `ControlHubSettingsPath` bindings. Disable `ObservePeerDuringRoomControls`; this case covers a single gateway instance.

The case runs three sequential Auto-to-Manual-to-Auto cycles. Before input, the room must be idle in Auto with a valid assigned schedule and an existing saved manual temperature. Each cycle uses separately named evidence, independently observes the hub and driver, and verifies restoration of the original room policy. If a transition initializes the active manual value differently, the existing guarded compensation restores the captured saved value before returning to Auto. Missing manual targets are refused before input; this repeated test does not permit initialization even if another fixture's initialization flag is enabled.

The driver lifetime, physical room identity and completed-command count remain tied to the first cycle. Unrelated settings changes, unexpected commands, failed input or unconfirmed restoration stop the run; later cycles do not adopt new household settings as a baseline or replay an uncertain request. Cancellation still allows the existing cycle's separately bounded restoration before the outer workflow decides whether to release its reservation.

Allow at least 15 minutes in the workflow for three cycles and cleanup. This is an outer execution bound, not response-time acceptance. It does not claim rapid taps, observation of every transient busy state, deliberate error recovery, other manual-target starting states or multiple-instance acceptance. Those require separate cases and measured evidence.

The fixture and its offline fault tests are prepared source. No passing hardware result is claimed. Run only after any active endurance reservation has ended and final observation/release have been confirmed, using fresh private bindings and producer pins for the immutable candidate.
