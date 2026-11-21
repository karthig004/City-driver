# CITY DRIVER: UNDERCOVER - Project Status

Unity 6000.6.9f1 | URP 17.6 | Input System 1.20.1 | Android first (30 FPS), Web-compatible architecture.
Version control: Unity Version Control (existing cloud repo). No Git. `cm` CLI not installed: check-ins are made in the Unity window.
Updated: 2026-11-15 (05:05)

## Honest scope statement
The master brief (1.5-2 km city, realistic vehicles, traffic AI, police/gang AI, weather, audio, Tamil/English, full career,
garage, weapons, APK) is multi-month studio work. It is being built in the brief's priority order and every item below is
marked DONE only when it was compiled AND tested. Visuals are limited to what free assets + procedural generation can give;
anything placeholder is labelled placeholder.

## Verification vocabulary
- BUILT = code exists and compiles.  TESTED-EDIT = automated Edit Mode test passes.  TESTED-PLAY = verified in Play Mode.
- Nothing is marked DONE without TESTED.

## Systems (working / incomplete / missing)
| System | State |
|---|---|
| Project setup (IL2CPP, ARM64, landscape, package id, Low/Medium/High URP) | DONE, verified on disk |
| Android toolchain + APK | See 'Final APK' below. Never run on a physical Android device. |
| Editor bridge (compile, run commands, read logs, screenshots) | DONE; no longer recompiles during Play |
| 4x4 test city (placeholder boxes), 8 mission markers | DONE but visually unacceptable; must be replaced by the large city |
| Vehicle physics (Rigidbody + 4 WheelColliders, stability layer) | TESTED-EDIT (46 checks); TESTED-PLAY: user driving feedback pending |
| Camera | TESTED-PLAY (103/103 run): four views switched by the CAM button or Settings - chase (measured 7.0 m behind, 2.6 m up), far chase (11.5 / 4.6 m), bonnet (1.2 m, hood visible), top-down heading-up (26 m up). Chase pulls back and rises with speed, widens FOV, looks ahead; collision only against buildings and road (traffic, pedestrians and the player car no longer push it). Subjective feel on a phone: NOT tested. |
| Driving HUD | TESTED-PLAY: wallet, level, XP bar, MISSIONS / MENU / RADIO / SETTINGS buttons, mission banner, subtitles, toast, speed + gear, horn and handbrake buttons, minimap. Layout checked at 6 aspect ratios (no overlap / off-screen). |
| Waypoint markers (replace giant green beacons) | DONE: verified in real Game view screenshots (small diamond + label, hidden unless active); 8 marker ids/positions unchanged (logged `unchanged: True`) |
| Procedural sedan | BUILT + seen in a real Game-view capture (rear view): lofted body with tapered cabin, glass/roof, grille, bumpers, headlamps, tail lamps, mirrors, door cuts, spoked wheels, taxi sign; brake/reverse lights via VehicleLights (not yet observed in Play). Stylised procedural model, NOT a realistic licensed model. Physics prefab components unchanged. |
| Core logic: save, wallet (idempotent transactions, debt), XP, career (30 missions, upgrade gates), Shadow Pass rules, garage upgrades | TESTED-EDIT: 56/56 checks. Not yet wired to UI/gameplay |
| Large city (1.92 km road network, 7 districts, POIs) | BUILT: 97 chunk scenes (240 m) streamed additively around the player, prefab buildings with LODGroup, CC0 facade textures, trees, signals, streetlights. Road graph TESTED-EDIT 36/36. Streaming works in Play; load times depend heavily on free RAM (about 1-2 GB free on the dev PC). |
| Road graph routing + GPS + maps | TESTED-EDIT (A* == Dijkstra) and TESTED-PLAY: routing, turn text, re-route, minimap, full map tap-to-navigate. ALIGNMENT CHECK (new): of 24 road-graph points near the car, a real 3D road collider lay under 24 and the map texture drew a road at 24; the minimap is centred on the car and rotates with its heading. If the map still looks wrong on screen, the specific mismatch is not yet identified. |
| Traffic, pedestrians | TESTED-PLAY (64/64 run): 8 moving cars (7 in motion), 6 parked, 12 pedestrians (all walking); from the player camera 2 moving cars, 1 parked, 7 pedestrians were visible at that moment; left-lane audit 0 violations of 2 cars; 0 of 12 pedestrians in the carriageway; counts stay stable while driving and agents recycle. Counts are capped by the Low quality level used in the Editor (10/8/16 at Medium+). NOT reaching the 8-12 visible cars target. No pedestrian crossings; signals are a timing model not tied to the visible signal heads. Stylised box-model cars/people, not realistic. |
| Police / gang AI, heat | TESTED-PLAY (Shadow Run, Level 4 plan): gang car spawns behind the player 120+ m away and never in front, chases over the road graph, closes in (221 -> 159 m), rams (condition 100% -> 96%); escape by distance (230 m for 8 s); police box-in busts a stopped player -> mission fails, penalty applied, captured flag blocks new Shadow Run calls until bail; roadblock of two police cars set on the route ahead; smoke launcher works with a 12 s cooldown; heat meter 0-5 in the HUD. Plans for levels 4-10 follow the brief's table (levels other than 4 not run in a test). Simple kinematic chasers, not physics cars; no interception of traffic. |
| Garage, vehicles, upgrades | TESTED-PLAY: upgrades change the real physics (engine level 5: torque 1300 -> 1690, top speed 140 -> 168 km/h, speed after 3.5 s 30.9 -> 40.2 km/h; armour level 5 cuts damage 13% -> 5%; headlight beam range; tyres, brakes, suspension scale the stats); 6 vehicle classes with level gates (supercar bought at level 9, 59.3 km/h vs 30.9 km/h compact after 3.5 s); paints with a Level 2 unlock. Fuel tank upgrade extends range. |
| Day/night, weather | TESTED-PLAY: morning / day / sunset / night presets (night: sun 0.26, headlights on with two beams), rain and heavy rain (1,870 particles, grip x0.70, fog end 156 m, rain sound), fog (end 94 m), clear restores everything. Wet-road reflections are NOT implemented. Streetlight bulbs do not light up at night (their material is shared). |
| Audio | TESTED-PLAY by objective checks only (nobody has listened to it): fully synthesised in code on a background thread: engine (rpm pitch), tyres, wind, city murmur, collisions, UI click, horn, rain, thunder, police siren, suspense music for Shadow Run, card chime, and a car radio with 3 synthesised stations. Voice: device text-to-speech hook (Android) - NOT verified on a phone; no recorded voices, no lip-sync. Tamil: NOT IMPLEMENTED (Unity's standard UI text cannot shape Tamil; it needs UI Toolkit text or TextMeshPro with a Tamil font and shaping). |
| Mobile touch controls | TESTED-PLAY via the test hook `TouchControlsPanel.SetTestPointers` (not real Input System touches; physical phone NOT tested): accelerate, brake, reverse, camera, handbrake, plus THREE steering modes selectable in Settings: arrow buttons, on-screen steering wheel (drag; measured +0.67 / -0.67 at the sides, springs back to 0.00) and tilt (code path runs without an accelerometer; real tilt direction/feel UNVERIFIED on a phone). Steering sensitivity setting. |
| Mission flow + trips + UI | TESTED-PLAY (166/166 run, three consecutive full runs after the last fixes): trips are planned on the road network (measured 1,096 m Standard / 1,956 m Epic, pickup 284 m away, distance bonus paid, Trip Length setting), First Ride end to end with wallet / XP / save / replay / abandon, long-trip GPS, menus (main, missions, garage, vehicles, wallet, settings, weapon shop, result). Screenshots Bridge/gv_demo_00..34. English only. |

## Plan in priority order (autonomous; each step compiled + tested before moving on)
1. Finish Phase A (markers) -> verify with Game view screenshots.
2. Logic foundation (save/wallet/career) - parallel, needs no Editor.
3. Early debug APK of the current state to de-risk the Android pipeline.
4. Large city: road-graph-driven generator, districts, modular buildings, LOD/culling, additive streaming.
5. Visual foundation: PBR textures (CC0), lighting/post, sedan mesh, camera framing.
6. Driving: touch controls, traffic on the road graph, GPS + minimap + full map.
7. Career gameplay wired to the logic layer; garage + upgrades.
8. Shadow Run: night, police/gang pursuit.
9. Weather, audio, localisation.
10. Final APK, profiling, release notes.

## Road network (generated, saved at Data/World/CityRoadGraph.asset)
343 junctions, 576 road segments, 61.4 km of road, 1.92 x 1.92 km, 161 signalised junctions. Highway ring 22 m / 3 lanes each way,
arterials 14 m / 2 lanes, collectors and locals 9 m. The original 80 m downtown grid is preserved, so the 8 mission markers did not move.
15 points of interest (hospital, police, safe houses, fuel, repair, garage, luxury, nightclub, cinema, hotel, warehouses).

## New in the latest round (all verified by the 166-check Play test unless stated)
- Shadow Pass cinematic: card animation, zoom, day-to-night with rain, suspense music, headlights, nightclub camera shot, secret mission notification (6 of 6 stages).
- Weapon shop (after Level 5): Smoke Launcher and EMP Pulse (optional, work only in a chase). Bail to clear the "captured" state.
- Fuel: 1% per 150 m, refuel at petrol stations/garage (Rs 3 per %), tow (Rs 400), saved.
- HUD skin: round minimap with N, speedometer arc with rpm needle, fuel/health bars, time-of-day buttons, heat meter, weapon and Shadow Pass buttons.
- People: faces, hair, turban/cap/bun variants, saree/kurta/school outfits. Trees: layered individual-leaf textures + bark.
- ROOT CAUSE found for the brown tree canopies: an old bug in the mesh-saving helper flattened multi-material meshes, so leaf cards were drawn with the bark material (solid brown). Fixed in CityAssetFactory.SaveMesh and the 97 chunk scenes were rebuilt (171 s); screenshots now show green canopies.
- Street furniture along sidewalks (68 props active in the test).

## Final APK (updated after the last build; see the build report in the chat)
The last verified APK is whatever `Builds/Android/CITY_DRIVER_UNDERCOVER.apk` was when `aapt2`/`apksigner` were run (results are reported in the conversation, not assumed here). Debug-signed, arm64-v8a, IL2CPP, min SDK 26.

## Known bugs / risks
- The main menu opens at launch in builds and pauses the game; its buttons are verified only through the Editor test, not on a phone.
- **Editor Play automation was unreliable earlier tonight** (resolved for the final runs: Unity kept active, 64/64 twice): the player loop often stalls at frame 1 (time 0.0) unless the Unity window is active; free RAM was 0.6-2 GB of 16 GB (Chrome ~3.5 GB, VS Code ~1.8 GB). Consequence: the end-to-end mission/UI Play test and the demo screenshots (main menu, garage, mission result) were NOT captured. Last fully clean Play run: 35 of 41 checks (6 touch checks failed after cars were placed too close; fixed in code, not re-run).
- Not implemented: recorded music/voice, lip-sync, Tamil text, wet-road reflections / post-processing, photoreal models, traffic pedestrian crossings, real-phone verification of anything.
- Trees: fixed (see above); canopy meshes are still simple leaf cards, not photoreal.
- Audio synthesis was blocking the first game frame for ~50 s in the Editor; moved to a background thread (fixed, verified).
- APK not yet run on a real Android device; 30 FPS on a phone unproven; individual trees are ~80 triangles (the earlier ~1,700 figure was per merged chunk renderer); the player sedan is a lofted procedural model but still low-poly.
- Unity is sometimes left in Play Mode by the user; all automation then waits.
- Bridge console capture can stop after a mid-Play domain reload (use Logs/Editor.log as fallback).
- Free realistic vehicle models are scarce and usually need a login to download; procedural fallback is the default.
- 1.5-2 km city on Iris Xe / mobile needs streaming; sizes will be set from measurements, not assumed.
- Game view window is small/ultra-wide (880x377 captures).

## Asset sources and licences
See ASSET_LICENCES.md (ambientCG CC0 textures).

## Performance measurements (dev PC, Intel Iris Xe, Editor Game view; NOT a phone)
- Shadow casters actually drawn per frame (UnityStats) were 105-166, not 1,924: shadow-distance culling already limits them. Draw calls rose from 474 to 568 at spawn after the first sedan, before its details were merged per material; not re-measured after merging.
- Per-view cost in 9 Game-view shots (before the sedan and population): 255-621 draw calls (includes shadow pass), 63k-209k triangles visible, 14-26 set-pass calls, average frame 4.7-8.0 ms (125-215 fps in the Editor). The earlier "2.4M triangles" is the total of all loaded chunks, not what the camera draws.
- Streaming: 316 chunk loads, average 0.20 s, slowest 1.14 s (warm, enough free RAM). With ~1-2 GB free RAM earlier loads took tens of seconds.
- Render profile (Play test, 9 chunks loaded): 1,942 active MeshRenderers, 1,924 shadow casters, 759 LODGroups, 48 distinct materials. Largest triangle sources: tree bark+canopy (69 renderers, ~120k tris, ~1,700 tris/tree) and the building prefab instances (~375k tris summed over all LOD levels).
- GPU instancing experiment: enabled on all 87 project materials and re-measured: draw calls IDENTICAL (URP's SRP Batcher already batches these). Reverted. No other optimisation has been applied or measured yet.
- **No measurement exists on a phone, so the 30 FPS target is UNPROVEN.**
- Existing: URP Low/Medium/High assets, shadow distance 25/40/60 m, chunk streaming (load 230 m, unload 290 m), per-building LODGroups.
- Not done: occlusion culling, static batching (chunk meshes are already combined), texture-compression audit, traffic/pedestrian culling beyond distance recycling, fewer shadow casters, cheaper trees.

## Runtime exception testing
PlayTests hooks `Application.logMessageReceived` for the whole run and separates game-code errors from test-runner errors (stack contains `PlayTests`). Latest runs: 64/64 checks twice, 0 game-code errors, 0 test-runner errors, covering touch driving, GPS routing and re-routing, minimap, full map, traffic/pedestrian audits, the passenger mission with save/load, and streaming 8 new chunk scenes after moving the car about 1 km. Not covered: long sessions, scene switching to other scenes (only CityDriving is wired), a physical device.

## Phase log (original numbering)
1 inspect: done | 2 project: done | 3 city: done (placeholder) | 4 vehicle: built+edit-tested | A markers: applied, visual check pending


## Update 2026-11-15 (afternoon): cinematic intro, icon fixes, Play Store preparation, promo trailer
Verified in the Unity Editor (nothing was tested on a physical phone):
- **Play tests: 174/174 pass** (new section "App-open cinematic intro": plays, post-processing active, camera driven, logo shown, reaches the hero shot, tips rotate, ends by itself and hands the camera back).
- **Intro** (`IntroSequence.cs`): studio card, three continuous camera shots over the live city (aerial, swoop down the road, hero orbit of the player's car), bloom / grade / vignette on the 3D picture (removed afterwards), light rays, dust bokeh, twinkling stars, flash on the logo reveal, glowing pill loading bar with real progress + percentage, rotating tips, "TAP TO START". HUD hidden during the intro. Shown always in builds; in the Editor only when `Bridge/show_intro.txt` exists.
- **HUD icons:** the settings gear is redrawn as a cog (it read as a sun), pedal grip lines thinned, every round icon turns blue when pressed.
- **Play Store prep** (`PlayStorePrep.cs`, menu City Driver > Play Store): launcher icon generated from the shield emblem and assigned (adaptive/round/legacy + 512 store icon), release settings, release APK and AAB builds. The AAB is signed only if you supply your own keystore through environment variables; otherwise it is named `..._NOT_UPLOADABLE_debug_signed.aab`.
- **Found and fixed:** the old APK was a Development (debuggable) build, which Google Play rejects. Release builds are now non-debuggable. The release APK still declares INTERNET (auto-added by Unity).
- Built: `Builds/Android/CITY_DRIVER_UNDERCOVER_release.apk` (54.2 MB, arm64, target SDK 36), `..._NOT_UPLOADABLE_debug_signed.aab` (54.6 MB). `CITY_DRIVER_UNDERCOVER.apk` (82 MB, 12:33) is the OLD development build without the intro.
- **Promo trailer v2** (`TrailerCapture.cs`, 2026-11-15 evening): 65.3 s, 1920x1080, 30 fps, 2.39:1, about 35 cuts, recorded from the real game (ACES + teal/orange grade, bloom, depth of field, motion blur, grain, slow motion, flashing police lights, moon fill light) with a score synthesised from the recorded shot timeline (`make_score2.py`). Story told only with pictures: a calm day and a cold wet night on the same street, a day/night flicker, and a title that splits the same way ("CITY DRIVER" in daylight, "UNDERCOVER" in the dark). Muxed into an AVI by a Python script; Windows' media stack opens it (65.3 s, 1920x1080, audio+video verified). The pursuit cars are held in a controlled convoy (real gang cars ram and flip the car). Still low-poly: not photoreal. See `PROMO_AND_STORE_COPY.md`.
- Docs: `PLAY_STORE_READINESS.md`, `PROMO_AND_STORE_COPY.md`, `PRIVACY_POLICY_TEMPLATE.md`.

Still open: no physical-device test; no release keystore (you must create it); Tamil, recorded voice and photoreal assets are not done; ffmpeg could not be downloaded in this environment, so the trailer is Motion-JPEG (large file, re-encode before upload if you wish).

**Trailers, final state (2026-11-15 night):** two films, both with on-screen text, spoken dialogue (Windows David / Zira / Hazel voices, synthetic) and dialogue subtitles: `CITY_DRIVER_UNDERCOVER_trailer_classic.avi` (first version rebuilt, 53.5 s) and `CITY_DRIVER_UNDERCOVER_trailer_cinematic.avi` (65.3 s). Both verified to open in Windows' media player with audio and video. Not yet listened to or watched end to end by me; the voices are speech-synthesiser quality, and about 0.03% of audio samples touch full scale.

**Overview film (2026-11-15 night):** `CITY_DRIVER_UNDERCOVER_overview.avi`, 77.6 s: calm, clean, classical normal mode with feature callouts and a narrator, then a thriller Shadow Pass, then peace and the title. Verified to open in Windows' media player with audio and video; not watched or listened to end to end by me. Synthetic speech voices; low-poly visuals. Details in `PROMO_AND_STORE_COPY.md`.
