# Crestron submission help

[help-content.json](help-content.json) is public source for the driver's help document, using the official Crestron help template and the source tools in [CrestronHomeDevTools](https://github.com/oznetmaster/CrestronHomeDevTools). The builder and renderer are currently local source changes, not released tooling. See its `docs/submission/HelpBuild.md` for the content format and commands.

This is a review draft for the current driver, not an approved portal submission. It is not embedded in the released driver package. Ordinary builds retain their existing package name and do not require document tools. The source manifest now contains the approved public support email; runtime behavior, GUID and driver version are unchanged by this preparation.

The content covers the root Home options page, the room thermostat, schedule selection and editing. Its declared UI inventory also includes schedule/day/time selector dialogs. The remaining model/firmware facts, screenshots, license inventory and final candidate version are explicitly listed in `pending`. Final-mode help generation rejects pending items and missing declared UI screenshots; use `--draft` and a `.review.docx` output for review.

The public support email is `support@marvelous.com`. Keep the submission correspondence address, credentials, actual device bindings, source signature image and private screenshots outside this repository. Only approved public figures should be referenced by the content file, using relative paths and SHA-256 digests.

The help must identify this driver's MIT License with Commons Clause accurately. The generator's separate MIT license does not change the driver's license.

## Opt-in submission build

The driver project now imports the reusable help packaging targets when `CrestronSubmission=true` and `SubmissionToolsDirectory` points to a reviewed CrestronHomeDevTools source checkout's `tools/submission` directory. Configure the explicit Python, LibreOffice and pinned official template paths described in DevTools' `docs/submission/HelpBuild.md`. Keep machine-specific settings in the privately excluded local targets file or pass them to the build command.

```text
dotnet build WiserHeatCrestronDriver/WiserHeatCrestronDriver.csproj -c Release -p:CrestronSubmission=true -p:DeployAfterBuild=false -p:SubmissionToolsDirectory=TOOLS_DIRECTORY -p:SubmissionPython=PYTHON_EXE -p:SubmissionSoffice=LIBREOFFICE_EXE -p:SubmissionHelpTemplate=OFFICIAL_DOCX -p:SubmissionHelpTemplateSha256=PINNED_SHA256
```

Replace the uppercase placeholders with local paths and the reviewed template digest; quote arguments containing spaces. These source tools are not in the published DevTools package yet. Do not enable the option in the ordinary release workflow until a complete candidate has passed validation.

This option uses `NeilColvin_Thermostat_WiserHeat_IP_V2` as the matching package/DLL/help basename. It preserves the driver GUID. Release version preparation remains authoritative, and the content's four-component version must match the prepared manifest. Before deploying a future submission candidate, select a new version and update the content; do not replace the bytes of an already published or tested version.

The build generates final-mode help before compilation, adds the PDF after copying the normal package assets, then verifies its bytes and the generated package identity after ManifestUtil. Build receipts are retained under `obj/submission-help/<unique-id>` and are not included in the package. Merge and packaging failures cannot be ignored in submission mode. Test/design-time builds skip these steps.

The current content deliberately fails this option because its remaining facts and UI screenshots are incomplete. A real invocation confirmed that rejection without changing the manifest version; the ordinary test build still succeeds. No completed submission package has yet been built, tested or deployed by these hooks. Completing the content, visually reviewing the final PDF, building with ManifestUtil and connecting candidate-bound hardware evidence remain required.
