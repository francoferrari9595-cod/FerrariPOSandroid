# FerrariPOS Android replacement — 2026-09-24

The Android reference supplied by the user is `FerrariPOS-Manager-Android.apk`.

Verification performed before packaging:
- SHA-256 of supplied reference APK: `2707bf5ff32b605221778776621cdf5aa81836933c6c991c911e330a83649ef6`
- The repository's previous `FerrariPOS.Manager.Android/app/build/outputs/apk/debug/app-debug.apk` had the exact same SHA-256 and byte-for-byte content.
- Therefore the Android build artifact already corresponded exactly to the supplied reference application.
- The Windows project was not modified.
- `FerrariPOS.Manager.Android/gradlew` is packaged with executable permissions for Unix/GitHub environments.

The Android source project is retained because an APK alone cannot safely replace a compilable Android source tree. The existing Android source is the source associated with the exact reference APK in this repository.
