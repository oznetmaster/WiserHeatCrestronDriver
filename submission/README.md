# Crestron submission help

## Driver-specific test coverage

[extension-coverage-plan.json](extension-coverage-plan.json) is a draft breakdown of every item in the official Extension self-test form. It covers the gateway Home tile, managed-room thermostats, all declared pages/controls and conditional schedule slots, configuration, restoration, outages, endurance and repeated multi-instance checks. The source [DevTools coverage generator](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/CoveragePlanning.md) expands it into a draft policy, form mapping and execution contract. It checks the recorded UI/behavior/configuration source hashes and rejects omitted UI targets or official items.

This is planned coverage, not a test result. Producer bindings, real Release candidate evidence, visual review and policy approval remain incomplete. The generated contract records response deadlines and restoration requirements, which future producers must enforce; generic hash/evidence validation alone does not measure them. Repeated assertions can share a controlled sequence and captures when each assertion is actually verified.

Two actual thermostat rooms can test managed-child isolation with state restoration. Whether those count as the formal two-instance test for this platform driver remains unresolved; do not automatically equate two children with two gateway instances. Keep actual room names, device IDs, hub credentials, emulator settings and evidence in private local bindings. A dedicated test room is useful for temporary installation/removal fixtures but is not itself an independent physical thermostat or hub.

Outage cases require independent control of the specified test equipment, with recorded loss/recovery timings. A Home reboot does not replace the physical power-outage test. Endurance requires periodic functional observation over at least 24 hours, not just elapsed timestamps. Unsupported-control proposals need retained absence evidence across the final candidate's runtime variants; they are never silently passed.

Source changes require review of the coverage snapshot before updating its hashes. No ordinary driver behavior, build/deploy workflow or published version changes as a result of this planning file.

## Help source

[help-content.json](help-content.json) is public source for the driver's help document, using the official Crestron help template and the source tools in [CrestronHomeDevTools](https://github.com/oznetmaster/CrestronHomeDevTools). The builder and renderer are source tools in DevTools; use the reviewed source revision providing website support. See its `docs/submission/HelpBuild.md` for the content format and commands.

This is a review draft for the current driver, not an approved portal submission. It is not embedded in the released driver package. Ordinary builds retain their existing package name and do not require document tools. The source manifest now contains the approved public GitHub support website and an empty Email field; runtime behavior, GUID and driver version are unchanged by this preparation.

The content covers the root Home options page, the room thermostat, schedule selection and editing. Its declared UI inventory also includes schedule/day/time selector dialogs. The remaining model/firmware facts, screenshots, license inventory and final candidate version are explicitly listed in `pending`. Final-mode help generation rejects pending items and missing declared UI screenshots; use `--draft` and a `.review.docx` output for review.

Public support is provided through [the GitHub repository](https://github.com/oznetmaster/WiserHeatCrestronDriver) and its [issue tracker](https://github.com/oznetmaster/WiserHeatCrestronDriver/issues). No public support email is included. Keep the submission correspondence address, credentials, actual device bindings, source signature image and private screenshots outside this repository. Only approved public figures should be referenced by the content file, using relative paths and SHA-256 digests.

The help must identify this driver's MIT License with Commons Clause accurately. The generator's separate MIT license does not change the driver's license.

## Opt-in submission build

The driver project now imports the reusable help packaging targets when `CrestronSubmission=true` and `SubmissionToolsDirectory` points to a reviewed CrestronHomeDevTools source checkout's `tools/submission` directory. Configure the explicit Python, LibreOffice and pinned official template paths described in DevTools' `docs/submission/HelpBuild.md`. Keep machine-specific settings in the privately excluded local targets file or pass them to the build command.

```text
dotnet build WiserHeatCrestronDriver/WiserHeatCrestronDriver.csproj -c Release -p:CrestronSubmission=true -p:DeployAfterBuild=false -p:SubmissionToolsDirectory=TOOLS_DIRECTORY -p:SubmissionPython=PYTHON_EXE -p:SubmissionSoffice=LIBREOFFICE_EXE -p:SubmissionHelpTemplate=OFFICIAL_DOCX -p:SubmissionHelpTemplateSha256=PINNED_SHA256
```

Replace the uppercase placeholders with local paths and the reviewed template digest; quote arguments containing spaces. These Python source tools are not embedded in the DevTools NuGet package or console ZIP. Website support requires the updated source checkout. Do not enable the option in the ordinary release workflow until a complete candidate has passed validation.

This option uses `NeilColvin_Thermostat_WiserHeat_IP_V2` as the matching package/DLL/help basename. It preserves the driver GUID. Release version preparation remains authoritative, and the content's four-component version must match the prepared manifest. Before deploying a future submission candidate, select a new version and update the content; do not replace the bytes of an already published or tested version.

The build generates final-mode help before compilation, adds the PDF after copying the normal package assets, then verifies its bytes and the generated package identity after ManifestUtil. Build receipts are retained under `obj/submission-help/<unique-id>` and are not included in the package. Merge and packaging failures cannot be ignored in submission mode. Test/design-time builds skip these steps.

The current content deliberately fails this option because its remaining facts and UI screenshots are incomplete. A real invocation confirmed that rejection without changing the manifest version; the ordinary test build still succeeds. No completed submission package has yet been built, tested or deployed by these hooks. Completing the content, visually reviewing the final PDF, building with ManifestUtil and connecting candidate-bound hardware evidence remain required.

## Dependency licenses and notices

`dependency-notices.json` records the actual merged dependency DLL hashes, package versions, original copyright metadata and reviewed license/NOTICE documents in `license-sources`. Those documents retain their original bytes and notices, including the WiserHeatAPIv2 authors and YamlDotNet's separate libyaml license. They are third-party notices, not project-owned source files.

The submission build generates `THIRD-PARTY-NOTICES.txt` from that inventory after copying package assets. It refuses changed, missing or additional merge dependencies and verifies the exact notice file in the resulting package. The project rejects older tooling that lacks this check. Ordinary/test builds do not invoke the submission renderer or this notice-generation hook.

After a dependency update, review its package license and upstream notices before updating the inventory; a new DLL hash alone is not a license review. The current inventory matched all 18 dependency DLLs in the development build. Inclusion by ManifestUtil in the final Release candidate remains to be verified. See [the reusable notice-build documentation](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/DependencyNotices.md).
