# Android device test procedure (physical phone)

STATUS: **NOT YET RUN.** Nothing below has been verified on a physical phone. Fill in the Result column yourself.

## What has and has not been verified
| Input path | Verified? |
|---|---|
| Touch panel logic fed through the test hook `TouchControlsPanel.SetTestPointers` (Editor Play tests, 32/32) | YES, in the Editor on the dev PC |
| Real Input System touches from a physical touch screen (`Touchscreen.touches`, multi-touch) | **NO** |
| Editor mouse acting as one finger | Works in code path, not separately tested |
| Anything on a real Android device | **NO** (APK never installed; PC emulator crashed in ARM translation) |

## Install
1. Phone: Settings > About > tap Build number 7 times > Developer options > enable USB debugging. Needs Android 8.0 (API 26) or newer and a 64-bit ARM CPU.
2. Connect by USB, accept the debugging prompt.
3. From a terminal (adb is in Unity's SDK):
   `"C:\Program Files\Unity\Hub\Editor\6000.6.9f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" install -r "C:\Users\byuva\My project\Builds\Android\CITY_DRIVER_UNDERCOVER.apk"`
   If `platform-tools` is missing, copy the APK to the phone and open it (allow "install unknown apps").
4. Optional log capture while testing: `adb logcat -s Unity` > save as `device_log.txt`.

## Test steps (landscape, game starts in the city)
| # | Action | Expected | Result |
|---|---|---|---|
| 1 | Launch app | Splash, then city; no crash; HUD + 6 buttons visible inside the safe area | |
| 2 | Hold GAS | Car accelerates, throttle ramps over about 0.25 s | |
| 3 | Hold GAS and, with a second finger, STEER RIGHT | Car turns right while still accelerating (two fingers) | |
| 4 | Same with STEER LEFT | Car turns left | |
| 5 | Release everything | Throttle falls, steering recentres | |
| 6 | Hold BRAKE at speed | Car stops | |
| 7 | At standstill hold REVERSE | Car goes backwards, gear shows R | |
| 8 | Tap CAMERA RESET | Camera snaps behind the car | |
| 9 | Open the full-screen map; tap a point of interest | Route drawn, instruction banner appears | |
| 10 | Drive off the route | Route recalculates within ~3 s | |
| 11 | Clear route button | Route disappears | |
| 12 | Drive 3 minutes across districts | No crash, no long freezes while chunks stream | |
| 13 | Rotate / lock-unlock the phone | Layout stays inside safe area | |

## Performance (needs the phone)
Enable a frame counter: `adb shell dumpsys gfxinfo com.karthilab.citydriverundercover framestats`, or note the HUD/Unity stats. Record: phone model, SoC, RAM, average FPS while driving downtown, FPS on the highway, any thermal throttling after 10 minutes. The 30 FPS target is **unproven** until this is filled in.

Report back: the Result column, `device_log.txt`, phone model.
