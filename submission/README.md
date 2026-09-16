# Crestron submission help

[help-content.json](help-content.json) is public source for the driver's help document, using the official Crestron help template and the source tools in [CrestronHomeDevTools](https://github.com/oznetmaster/CrestronHomeDevTools). The builder and renderer are currently local source changes, not released tooling. See its `docs/submission/HelpBuild.md` for the content format and commands.

This is a review draft for the current driver, not an approved portal submission. No runtime, manifest, driver version or package naming changes accompany it. It is not embedded in the released driver package. A normal driver build does not yet run the help generator.

The content covers the root Home options page, the room thermostat, schedule selection and editing. Its declared UI inventory also includes schedule/day/time selector dialogs. The remaining model/firmware facts, screenshots, license inventory and final candidate version are explicitly listed in `pending`. Final-mode help generation rejects pending items and missing declared UI screenshots; use `--draft` and a `.review.docx` output for review.

The public support email is `support@marvelous.com`. Keep the submission correspondence address, credentials, actual device bindings, source signature image and private screenshots outside this repository. Only approved public figures should be referenced by the content file, using relative paths and SHA-256 digests.

The help must identify this driver's MIT License with Commons Clause accurately. The generator's separate MIT license does not change the driver's license. Once complete, the help PDF will be included before building and testing the exact submission candidate. A portal-ready filename and the matching DLL/PDF identity remain separate packaging work.
