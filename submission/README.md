# Crestron submission help

The [Wiser submission status](ValidationStatus.md) records this driver's pilot, tested pages and remaining acceptance work. Shared tool documentation describes reusable contracts; Wiser-specific fixtures and results belong here.

The Android observation and control projects use released CrestronHomeNUnit.TestAdapter 1.12.0 and CrestronHomeDevTools 1.10.0. Use the released NUnit runner's `installed-tests` command for selected checks against an existing immutable candidate. It validates the selected installation and holds processor/emulator reservations; ordinary test discovery does not authorize hardware access. Keep the control project explicitly selected and supply its private opt-in settings only for the intended run. These test-tool versions do not change the frozen driver package.

## Driver-specific test coverage

[extension-coverage-plan.json](extension-coverage-plan.json) is a draft breakdown of every item in the official Extension self-test form. It covers the gateway Home tile, managed-room thermostats, all declared pages/controls and conditional schedule slots, configuration, restoration, outages, endurance and repeated multi-instance checks. The source [DevTools coverage generator](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/CoveragePlanning.md) expands it into a draft policy, form mapping and execution contract. It checks the recorded UI/behavior/configuration source hashes and rejects omitted UI targets or official items.

This is planned coverage, not a test result. Producer bindings, real Release candidate evidence, visual review and policy approval remain incomplete. The generated contract records response deadlines and restoration requirements, which future producers must enforce; generic hash/evidence validation alone does not measure them. Repeated assertions can share a controlled sequence and captures when each assertion is actually verified.

The planned multiple-instance test uses two actual gateway-driver instances on separate processors, both connected to the available physical hub. Record exact candidate identities, independent instance configuration and expected shared-device state propagation; disclose that only one physical hub was used. A clarification reply is not a prerequisite for progressing with this documented arrangement. Two thermostat rooms additionally test managed-child isolation, but are not counted as two gateway instances. Keep actual room names, device IDs, hub credentials, emulator settings and evidence in private local bindings. A dedicated test room is useful for temporary installation/removal fixtures but is not itself an independent physical thermostat or hub.

Outage cases require independent control of the specified test equipment, with recorded loss/recovery timings. A Home reboot does not replace the physical power-outage test. Endurance requires periodic functional observation over at least 24 hours, not just elapsed timestamps. Unsupported-control proposals need retained absence evidence across the final candidate's runtime variants; they are never silently passed.

Source changes require review of the coverage snapshot before updating its hashes. No ordinary driver behavior, build/deploy workflow or published version changes as a result of this planning file.

See the [coverage source review](CoverageReview.md) for the current snapshot and the additional live-definition behavior that still needs executable coverage. Validate against the reviewed tracked sources; local builds can change Debug manifest revisions and must not be silently substituted for that snapshot.

## Help source

[help-content.json](help-content.json) is public source for the driver's help document, using the official Crestron help template and the source tools in [CrestronHomeDevTools](https://github.com/oznetmaster/CrestronHomeDevTools). The builder and renderer are source tools in DevTools; use the reviewed source revision providing website support. See its `docs/submission/HelpBuild.md` for the content format and commands.

The help source now targets corrected submission candidate 1.3.11, which is not yet an approved portal submission or public release. Its illustrated PDF has been rebuilt and verified in the opt-in candidate package. The earlier 1.3.9 candidate failed processor startup; see [validation status](ValidationStatus.md). The published 1.3.8 package is unchanged. Ordinary builds retain their existing package name and do not require document tools. The candidate preserves the driver GUID and approved GitHub support website with an empty Email field.

The content covers the root Home options page, the room thermostat, schedule selection and editing. Its declared UI inventory also includes schedule/day/time selector dialogs. The observed development hub, processor and app versions are recorded without claiming support for untested earlier firmware. The seven public screenshots were approved and the candidate version is assigned, so `pending` is empty. Final-mode generation, actual package inclusion and visual review of all eleven help pages passed. An empty `pending` list establishes content completeness only; it is not passing hardware evidence or submission approval.

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

The rejected 1.3.9 candidate completed this opt-in Release build with official ManifestUtil 29. Its matching package/DLL/metadata/help names, embedded PDF bytes and dependency notices were verified. Every PDF page was visually inspected, preserving the official template and approved figure bytes. Those preparation checks did not detect its embedded-definition startup failure. Earlier candidate 1.3.10 passed package verification, processor gates and bounded Android controls/restoration checks. Candidate 1.3.11 adds temperature-unit and Off-state corrections with published WiserHeatAPIv2 1.1.2. Its merged entry point and eleven-page packaged help passed local checks; fresh processor/Android acceptance is still required for its changed bytes. Full control/variant acceptance, endurance and the final signed submission remain incomplete; successful packaging and this bounded inspection do not satisfy those requirements.

## Validation after the candidate is built

The help PDF must already be inside the package that hardware tests execute. Requiring a passing final-package test before generating that PDF would make the build impossible. Help content therefore identifies the observed development environment and the limited intended model; it does not certify that the final candidate has passed.

After the immutable candidate has been built, the submission workflow must retain evidence for all of the following:

- Package, DLL and help names, GUID, version, embedded help and dependency notice bytes match the build receipts.
- The recorded processor, hub and Android/app versions match the environment actually used for that candidate's tests. A material change requires review of the environment description and a new package if the help changes.
- The actual candidate is installed on the stated CCTFR6313G2 model and passes every applicable official requirement, including the remaining physical controls, restoration, isolation, outage and endurance checks. Other hub models and earlier processor firmware are not covered by the current development results.
- Complete observations map to the official self-test form, with the required private review, signature and delivery authorization.

These are mandatory submission acceptance obligations, not all implemented automated checks. The coverage plan and the shared evidence validator remain separate from the help builder. An empty help `pending` list proves neither passing hardware tests nor submission readiness. Do not change the tested package's help after the run; rebuild and repeat candidate-bound validation when its bytes change.

## Dependency licenses and notices

`dependency-notices.json` records the actual merged dependency DLL hashes, package versions, original copyright metadata and reviewed license/NOTICE documents in `license-sources`. Those documents retain their original bytes and notices, including the WiserHeatAPIv2 authors and YamlDotNet's separate libyaml license. They are third-party notices, not project-owned source files.

The submission build generates `THIRD-PARTY-NOTICES.txt` from that inventory after copying package assets. It refuses changed, missing or additional merge dependencies and verifies the exact notice file in the resulting package. The project rejects older tooling that lacks this check. Ordinary/test builds do not invoke the submission renderer or this notice-generation hook.

After a dependency update, review its package license and upstream notices before updating the inventory; a new DLL hash alone is not a license review. The current inventory matched all 18 dependency DLLs in the development build. Both the rejected 1.3.9 candidate and corrected 1.3.10 build verified inclusion and exact notice bytes after ManifestUtil. Retain this verification for the exact package later tested; do not transfer it to a rebuilt package without checking again. See [the reusable notice-build documentation](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/DependencyNotices.md).

Final package inclusion is a post-build check, not a pending help-content item: requiring it before help generation would prevent the package from ever being built. The opt-in build still refuses missing or changed notices after ManifestUtil. Retain that verification report with the final candidate; an empty help `pending` list cannot replace it.
