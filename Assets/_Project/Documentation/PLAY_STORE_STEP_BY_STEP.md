# How to publish CITY DRIVER: UNDERCOVER on Google Play (first-time guide)

Written for: a developer publishing their first Android game. Follow the steps in order. Nothing here needs you to code.
Honest note: the game has been tested in the Unity Editor, in a browser (WebGL) and built as an APK, but **not on a physical phone**. Do step 3 first.

## 0. What you need
| Item | Cost | Notes |
|---|---|---|
| Google Play Console developer account | one-time US$25 | https://play.google.com/console - needs a Google account, ID verification and a payment card |
| An Android phone (Android 8 / API 26 or newer) | - | to test the game before publishing |
| A public web page for your privacy policy | free | GitHub Pages, Google Sites or your own site (template: `PRIVACY_POLICY_TEMPLATE.md`) |
| The signed bundle `CITY_DRIVER_UNDERCOVER.aab` | - | made in step 2 |

## 1. Create the Play Console account (do this today, verification can take days)
1. Go to https://play.google.com/console and sign in with the Google account you want to own the game forever.
2. Choose **Personal** (you) or **Organisation** (a registered company). Pay the US$25 fee. Verify your identity (government ID, phone, address).
3. **Important rule for new personal accounts:** before you can release to everyone ("production"), Google requires a **closed test** with at least **12 testers who stay opted-in for 14 days in a row**. Ask friends / family / a community for 12+ Gmail addresses now. (An Organisation account is exempt. Check the Play Console help page, the numbers can change.)

## 2. Make the signed bundle (.aab) - inside Unity, no command line
1. Open the project in Unity. Menu **City Driver > Play Store > Release window (keystore + signed AAB)**.
2. **Create the upload keystore (one time):** fill name / organisation / country, choose a strong password twice, press **Create keystore...**, save it (e.g. Documents).
   **Back the .jks file AND the password up in two places** (USB + cloud). If you lose them you cannot update the game. (Play App Signing lets Google reset an upload key, but it is slow, so keep it safe.)
3. Press **Build signed AAB for Google Play** (takes 10-45 min). Output: `Builds/Android/CITY_DRIVER_UNDERCOVER.aab`.
4. Package name is `com.karthilab.citydriverundercover`. It can never change after the first upload. Version is `0.1.0` / versionCode `1`; every later upload needs a higher versionCode (Edit > Project Settings > Player > Android > Bundle Version Code).

## 3. Test on a real phone first (APK)
1. Copy `Builds/Android/CITY_DRIVER_UNDERCOVER_release.apk` to the phone (USB or cloud), tap it, allow "install unknown apps" once.
2. Play 15 minutes: steering wheel, pedals, drift (brake into a turn), a mission, a Shadow Run, rain, night, settings, rotate/background the app, check the frame rate and heat.
3. Write down anything wrong and tell me; fix before uploading.

## 4. Prepare the store listing material (all in `Bridge/store_assets/` or ready to copy)
- App name: **CITY DRIVER: UNDERCOVER**
- Short description (80 characters): `Drive an open city by day, run Shadow missions by night. Offline, no ads.`
- Full description: see `PROMO_AND_STORE_COPY.md` (edit freely).
- App icon 512x512 PNG: `Assets/_Project/Art/AppIcon/play_store_icon_512.png`
- Feature graphic 1024x500: `Bridge/store_assets/feature_graphic_1024x500.png`
- Phone screenshots (min 2, up to 8): `Bridge/store_assets/screenshot_*.png` (replace with real phone screenshots when you have them)
- Privacy policy URL: publish `PRIVACY_POLICY_TEMPLATE.md` (fill in your name and e-mail) on a public page and keep the link.

## 5. Create the app in Play Console
1. **All apps > Create app.** Name, default language (English), **Game**, **Free**. Accept the declarations.
2. **Dashboard > Set up your app** - answer each task:
   - **Privacy policy:** paste your URL.
   - **App access:** "All functionality is available without special access".
   - **Ads:** No ads.
   - **Content rating:** start the questionnaire, category *Game*. Be honest: stylised cartoon-level violence (police chases, gang cars, smoke/EMP gadgets), no blood, no real-money gambling, no user-generated content. Expect roughly an "Everyone 10+ / Teen" rating.
   - **Target audience:** 13+ (not a children's app). 
   - **Data safety:** the game stores progress **on the device only**, has no account, no ads SDK, no analytics and no INTERNET permission, so answer "no data collected, no data shared". (If you ever add ads or online features, update this form.)
   - **Government apps / financial features / health:** No.
   - **News app:** No.
3. **Store listing:** upload icon, feature graphic, screenshots; paste name and descriptions. Category: **Game > Racing** (or Simulation). Add your contact e-mail.
4. **Countries:** pick where to release (all, or start with India).

## 6. Closed test (required for new personal accounts), then production
1. **Testing > Closed testing > Create track.** Create a tester list (your 12+ e-mails) and **Create new release**: upload the `.aab`. When asked, **enrol in Play App Signing** (accept: Google keeps the final signing key, yours is only the upload key).
2. Release notes: "First test build". Save, **Review release**, **Start rollout to closed testing**.
3. Send testers the opt-in link (Testing > Closed testing > Testers). They install from Google Play. Keep 12+ people opted in for **14 consecutive days**.
4. After 14 days: **Dashboard > Apply for production access**, answer the short questionnaire about the test.
5. Then **Production > Create new release**, upload the `.aab` (a new versionCode if you fixed something), review, **Start rollout**. First review usually takes a few days (up to a week or more for new accounts).

## 7. After release
- Watch **Android vitals** (crashes, ANRs) in Play Console; fix and upload a new versionCode.
- Keep the keystore + passwords safe forever.
- Never upload a debug-signed or `NOT_UPLOADABLE` file: Google rejects it.

## 8. Common rejections and how this project avoids them
| Reason | Status here |
|---|---|
| Debuggable build | Release build is not debuggable (verified with aapt2 earlier; re-check the signed file: `aapt2 dump badging x.aab` is not applicable to aab, use the APK) |
| Target API too low | Automatic (API 36 at last check) |
| 64-bit | arm64-v8a only |
| Missing privacy policy | You must host the page (template provided) |
| Copyright | Only CC0 textures, code-generated art / audio, OFL fonts (Poppins, Bebas Neue). Logos and names are original. |
| Misleading content | Do not claim "real" cars/brands; names are fictional |
