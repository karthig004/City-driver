# CITY DRIVER: UNDERCOVER - Google Play readiness (audit of 2026-11-15, updated 2026-11-18)

**Update 2026-11-18:** new "CD" hexagon logo and launcher icon (regenerated), new store graphics in `Bridge/store_assets/` (feature graphic + 6 screenshots), a first-time guide `PLAY_STORE_STEP_BY_STEP.md`, a whole-game review `MISSION_AND_RELEASE_ANALYSIS.md`, and an Editor window **City Driver > Play Store > Release window** that creates your upload keystore and builds the signed AAB. The audit below is still accurate for the build settings (no INTERNET permission, target API automatic, arm64, not debuggable).


Package `com.karthilab.citydriverundercover` | version 0.1.0 (versionCode 1) | Unity 6000.6.9f1 | IL2CPP, arm64-v8a, min SDK 26, target SDK 36 (automatic).
"Verified" = checked against the built file or Unity's build report on the dev PC. **Nothing was tested on a physical phone.**

## 1. What is ready (verified)

| Item | Result |
|---|---|
| Release AAB (not debuggable, minify on) | Built, 54.6 MB: `Builds/Android/CITY_DRIVER_UNDERCOVER_NOT_UPLOADABLE_debug_signed.aab` |
| Target API | 36 (the APK manifest reports targetSdkVersion 36) |
| 64-bit | arm64-v8a only (Play requires 64-bit; 32-bit is not needed) |
| Launcher icon | Original adaptive + round + legacy icon generated from the "CD" hexagon emblem (`Assets/_Project/Art/AppIcon/`), plus `play_store_icon_512.png` for the Play listing |
| Release APK manifest (checked with aapt2) | `CITY_DRIVER_UNDERCOVER_release.apk`: target SDK 36, arm64-v8a, **not debuggable**, custom icon present. **It still declares `android.permission.INTERNET`**: Unity adds it automatically (setting "Force Internet Permission" is off; Unity's own modules still trigger it). The game never uses the network. Either declare the permission honestly in the Data safety form (no data collected) or add a custom Android manifest that removes it. |
| Debug flag | The old `CITY_DRIVER_UNDERCOVER.apk` was a *Development* build (debuggable). Google Play rejects debuggable builds, so it must never be uploaded. Use the release AAB. |
| Assets | Only CC0 textures (ambientCG) and code-generated art / audio. No third-party logos, no copied artwork. |

## 2. What only you can do

1. **Play Console developer account** (one-time registration fee, identity verification).
2. **Create your own upload keystore** (I never create or hold signing keys). In a terminal:
   `keytool -genkeypair -v -keystore upload-keystore.jks -alias upload -keyalg RSA -keysize 2048 -validity 10000`
   Keep the file and both passwords somewhere safe and backed up. Do not put them in the project or in version control.
3. **Build the signed AAB** (Unity closed, or via the Editor menu after setting these in the same terminal that starts Unity):
   `set CDU_KEYSTORE_PATH=C:\path\upload-keystore.jks`, `set CDU_KEYSTORE_PASS=...`, `set CDU_KEY_ALIAS=upload`, `set CDU_KEY_PASS=...`
   then menu **City Driver > Play Store > Build release AAB (Google Play)**. The output is `Builds/Android/CITY_DRIVER_UNDERCOVER.aab`.
   Without those variables the tool only produces the `NOT_UPLOADABLE` debug-signed bundle used for structure checks.
4. Enrol in **Play App Signing** when you create the app (Google holds the final signing key; yours is the upload key).
5. Fill the Play Console forms (answers below are suggestions: check them against what you actually ship).

## 3. Play Console answers (suggested)

- **App category:** Game > Racing / Simulation.
- **Content rating questionnaire:** the game has gang characters, police chases, and a weapon shop (smoke launcher, EMP) and car collisions. Answer honestly: stylised, no blood or gore, no real-money gambling. Expect a Teen-style rating, not "Everyone".
- **Target audience:** 13+ (not directed at children), so the Families policy does not apply.
- **Data safety:** the game stores progress on the device only (a local save file). No network permission, no ads SDK, no analytics, no account. Declare "no data collected / shared". **Only keep this answer if you add no SDK later.**
- **Privacy policy URL:** Play asks for one for most apps. A short template is in `PRIVACY_POLICY_TEMPLATE.md`; it must be hosted at a public URL you control.
- **Ads:** none. **In-app purchases:** none (the in-game "Rs" wallet is virtual and earned by playing).
- **Permissions:** `INTERNET` is declared by Unity but unused (see section 1). Nothing else is requested.
- **New personal developer accounts:** Google currently requires a closed test with a minimum number of testers for a minimum number of days before production access. The exact numbers change: read the Play Console "Testing requirements" page when you create the app.

## 4. Store listing material

- App name: `CITY DRIVER: UNDERCOVER`
- Short description (80 chars max): `Drive an open city by day, run Shadow missions by night. Offline, no ads.`
- Full description: see `PROMO_AND_STORE_COPY.md`.
- 512x512 icon: `Assets/_Project/Art/AppIcon/play_store_icon_512.png`.
- Feature graphic 1024x500: `Bridge/store_assets/feature_graphic_1024x500.png`.
- Phone screenshots (at least 2, 16:9 landscape, 1920x1080): ready-made crops are in `Bridge/store_assets/screenshot_*.png` (1600x804); real phone screenshots are better once you have them.
- Promo video: upload the trailer to YouTube, then paste its URL into the listing.

## 5. Still open before you publish (honest list)

1. **Test on at least one real Android phone.** Not done: touch, tilt steering, audio, haptics, battery, heat and frame rate (30 FPS target) are verified only in the Unity Editor.
2. Release manifest re-check was done (see section 1). Repeat it for the final signed build: `aapt2 dump badging` must show `targetSdkVersion:'36'` and no `application-debuggable`.
3. The synthesised voice and music are code-generated. Android text-to-speech is only a hook that has not been verified on a device. Tamil dialogue is not implemented.
4. Visual quality: low-poly procedural city. Licensed 3D models would be the biggest visible upgrade.
5. Version control: check in Scripts, Scenes, Art, Resources, Prefabs, Documentation and ProjectSettings. Exclude Builds, Bridge, Library, Temp and Logs. Back up your upload keystore outside the project.
