# Build and test

This repository uses [the shared Cake build component](https://github.com/oznetmaster/BuildAutomation/tree/ae6f311e530a18dc705971dcc655fc97de03e368) pinned in `component.json`. It is build tooling only; product dependencies and versions are unchanged.

The CI workflow retains its jobs, framework matrix, filters, prerequisites and artifact upload rules. Repository-owned recipes preserve the build/test/package commands. Publication, validation reuse, approvals, runner selection and hardware reservations remain in GitHub Actions.

Run the same task locally with .NET 10, Git and PowerShell:

```powershell
./build/Invoke.ps1 -Task TASK_NAME
```

For a matrix task, set its `BUILD_INPUT_*` environment values as shown in the workflow before invoking it. These are project/framework selections, not credentials. The first local invocation downloads the immutable component into ignored `artifacts/build-components/`.

Available tasks:

- `tests-tests-verify-discovery-coverage-guards`
- `tests-tests-run-nunit-driver-tests`
- `tests-tests-validate-independent-room-observation`
- `tests-tests-validate-read-only-endurance-producer`
- `tests-tests-verify-android-tests-remain-offline-without-a-workflow`
- `tests-tests-run-desktop-sdk-lifecycle-harness`
- `tests-tests-build-processor-package`
- `tests-tests-validate-renamed-driver-package-startup`
- `tests-tests-run-the-packaged-unit-suite-twice`
- `workflow-discovery-discover-verify-offline-test-explorer-discovery`

The shared component rejects missing/stale/empty/failed declared TRX results and writes one receipt per invocation beneath `artifacts/build-automation/`. It does not publish packages or invoke live hardware tests. Existing specialized scripts remain authoritative.
