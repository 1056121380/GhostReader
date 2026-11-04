# Release verification

Verified locally on Windows, 2026-11-04.

- Release build: succeeded, 0 warnings, 0 errors.
- Existing regression runner: 156 PASS checks, Failures: 0.
- Published self-contained executable: self-test passed.
- Tests use an isolated profile; personal settings and books are not published.
- Native window and taskbar behavior is environment-dependent; this is evidence for the local test session, not universal compatibility certification.

Reproduce with `scripts/build.ps1` then `scripts/verify.ps1`. CI compiles the app and test runner and publishes the package; interactive checks are local.
