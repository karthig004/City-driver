# CITY DRIVER: UNDERCOVER - Realism and "GTA feel" plan

Written 2026-11-17 from an audit of the project (code, PROJECT_STATUS.md, assets). Nothing here is implemented yet except the wheel fix (see 1.1).

## 1. What the game is today (audit)

| Area | Reality in the code | Why it feels basic |
|---|---|---|
| Player car | ONE procedural lofted sedan (SedanKit). All 6 "vehicles" in `VehicleCatalog` are this same mesh with a `bodyScale` and stat multipliers. | A supercar and a taxi look identical, only slightly squashed. |
| Wheels | Fixed this session: Unity's built-in cylinder is 2x the assumed size, so every old wheel was double size. New lathe tyre, rim, brake disc, spokes. | Was the "giant discs" problem. |
| Upgrades | `UpgradeType` Engine, Brakes, Tires, Suspension, Armor, FuelTank, Headlights; `MaxUpgradeLevel = 5`; multiplier maths in `VehicleTuning`. Stats change, the car never changes visually. | Upgrades are invisible. |
| Traffic, police, gang cars | `PopulationMeshes`: one merged box mesh per paint colour (`Car`), 3 `Pursuit` types. | Traffic looks like boxes. |
| People | `PopulationMeshes.Pedestrian(variant, pose)`: procedural people with faces/hair/outfits, a few poses, no skeleton. `Art/Characters` is EMPTY. | No real characters, no driver, no passengers you can see. |
| City | 1.92 km, 343 junctions, 61 km road, 97 streamed chunk scenes, procedural building prefabs + CC0 textures, trees, signals, 15 POIs. | Road network is solid; buildings are boxes with textures; too empty. |
| Map | Round minimap + full map drawn from the road graph (`RoadMapRenderer`, `MapBaker`), route + turn guidance. | Works, but no district names, blips, 3D/pause map, or in-world GPS line. |
| Police/gang AI | Kinematic chasers (not physics cars). | Chases lack contact, boxing-in, and wanted escalation. |
| Audio | All synthesised in code; no recorded engines, music, voices. | Biggest "cheap" tell after the models. |
| Not present | On-foot play, damage deformation, wet roads, real radio music, Tamil text, animated characters, ambient density. | GTA's feeling of a living city. |

Honest limit: realistic cars and humans cannot be written as code. They need real 3D models (downloaded under a free licence, bought, or modelled in Blender). Procedural code can only reach "good stylised".

## 2. Vehicle roster: 8 cars, 3 tiers, 3 upgrade stages

Assumption (change if you meant otherwise): "3 level" = 3 progression tiers of cars AND 3 upgrade stages per car. Names are original; no real brand logos.

| # | Tier | Car | Class | Price Rs | Player level to buy | Stock top km/h | Max top km/h after Stage 3 | Mass kg |
|---|---|---|---|---|---|---|---|---|
| 1 | 1 Street | City Taxi | Compact sedan | 0 | 1 | 130 | 150 | 1200 |
| 2 | 1 Street | Metro Hatch | Hatchback | 7,000 | 2 | 140 | 162 | 1050 |
| 3 | 1 Street | Metro Premium | Premium sedan | 15,000 | 3 | 160 | 184 | 1500 |
| 4 | 2 Pro | Apex GT | Sports sedan | 30,000 | 5 | 200 | 228 | 1450 |
| 5 | 2 Pro | Bulldog | Muscle coupe | 38,000 | 6 | 210 | 238 | 1650 |
| 6 | 2 Pro | Ridge Runner | Performance SUV | 45,000 | 7 | 190 | 214 | 1900 |
| 7 | 3 Elite | Sovereign LX | Luxury SUV | 70,000 | 8 | 185 | 208 | 2300 |
| 8 | 3 Elite | Vantage S | Supercar | 140,000 | 9 | 280 | 318 | 1350 |

Rule: an upgraded car in a lower tier may never beat the STOCK top speed of the next tier up (Street max 184 < Pro stock 190 ... enforced in code by a class cap).

### Upgrades: 3 stages per part, hard min and max
Parts: Engine, Brakes, Tyres, Suspension, Armour, Fuel tank, Headlights (7, as now). Stage 0 = stock (the MIN), Stage 3 = maximum (the MAX). `MaxUpgradeLevel` changes from 5 to 3.

| Part | Stage 1 | Stage 2 | Stage 3 (cap) | Hard limits (min .. max vs stock) |
|---|---|---|---|---|
| Engine | torque +8%, top +4% | +16%, +8% | +25%, +12% | torque 1.00 .. 1.25; top 1.00 .. 1.12 |
| Brakes | +12% | +24% | +40% | 1.00 .. 1.40 |
| Tyres | grip +5% | +10% | +18% | 1.00 .. 1.18 (wet grip still x0.70 in rain) |
| Suspension | stiffness +8% | +16% | +25% | 1.00 .. 1.25; ride height -0, -10, -20 mm visual |
| Armour | damage x0.85 | x0.70 | x0.55 | 1.00 .. 0.55 (never below 0.55, never invincible) |
| Fuel tank | range +15% | +30% | +50% | 1.00 .. 1.50 |
| Headlights | beam +20% | +40% | +60% | 1.00 .. 1.60 |

Cost per stage = % of the car's price: Stage 1 = 8%, Stage 2 = 18%, Stage 3 = 35%. Level gate: Stage 1 needs the car's unlock level, Stage 2 needs +2, Stage 3 needs +4 (capped at level 10).
Visual feedback (what makes upgrades feel real): Stage 1 = better rims, Stage 2 = body kit (lip, spoiler), Stage 3 = lowered stance, tinted lights, carbon trim, louder engine note.
Global safety caps in `VehicleTuning`: top speed <= 330 km/h, torque <= 2.2x base, brake <= 1.6x base, so mixed upgrades cannot break physics.

## 3. Characters required (with 3 model tiers each)

Assumption: "3 model" = each character needs 3 levels of detail (hero ~10k triangles, game ~4k, far ~1.2k) and each non-hero role needs 3 look variants so crowds are not clones.

### Story / hero characters (6) - unique, full body, face, 3 outfits where noted
1. The Driver (player, never shown in the film cut) - 3 outfits (taxi, street, night)
2. Mysterious Passenger (Shadow Pass holder)
3. Police Commissioner
4. Gang Lord (main antagonist)
5. Rival Racer
6. City Tycoon (VIP passenger / investor)

### Passenger roles (7 roles x 3 variants = 21): office commuter, college student, tourist, businessman, hospital visitor, club guest, emergency patient.
### Police AI (4 roles x 3 = 12): patrol officer, chase-unit officer, SWAT, roadblock officer.
### Gang AI (4 roles x 3 = 12): enforcer, biker, armed crew, gang driver.
### Race rivals (3): street racer, track king, speed queen.
### Ambient pedestrians (about 12 bodies, mix-and-match outfits): vendor, shopkeeper, jogger, cyclist, child, elder, worker, guard, couple, commuter, student, tourist.
Total: about 66 distinct looks built from about 8 base skeletons (slim/average/heavy male, slim/average female, child, elder) plus swappable outfits.
Animation set: idle, walk, run, talk, point/aim, hands-up, sit-in-car, open-door, get-in/out, hit reaction. Free source: Mixamo (Adobe login) or own rigging in Blender.

## 4. City: what to add for a GTA-style feel
Keep the 1.92 km streamed road graph (it works). Add:
- Districts (existing 7) with distinct identity: Downtown (tall glass, billboards), Commercial (shops, signs), Residential (houses, balconies, lanes), Industrial (cranes, containers), Entertainment (neon, cinema, nightclub), Highway ring (flyover, tunnel, bridge), Special (hospital, police HQ, safe houses).
- 3 landmarks per district (stadium, harbour/port, airport edge, tower, mall, temple/church/mosque as fits your setting).
- Building variety: from ~40 box prefabs to ~120 with proper window/door/AC/roof detail, emissive windows at night.
- Props: bus stops, signs, shop fronts, vending, bins, barriers, billboards, overpasses, crosswalk paint, lane arrows.
- Lighting: working streetlight bulbs at night, wet-road reflections, headlight cones, sky with clouds, bloom, fog layers.
- Density: visible cars 8-12, pedestrians 25-40 (with culling), parked cars on both sides, bikes and buses.
- Later: 3 km x 3 km city, water, elevated roads.

## 5. Map and navigation (GTA feel)
Radar: heading-up round minimap with district name under it, coloured blips (mission, shop, police, gang, fuel, friends), blip edge-clamp, wanted stars, zoom with speed.
Pause map: full-screen, pan and zoom, district labels, legend, tap to set waypoint, route preview with distance/ETA, fast-travel markers, "3D" tilt toggle.
In-world: GPS route line on the road, floating waypoint marker, turn arrows, road names.
Data already there: road graph, routing, turn guidance, POIs.

## 6. GTA-feel gap list (what players feel is missing)
P0 visuals: real car models, real wheels (done), real characters in cars and on the street, better city lighting and wet roads.
P1 world life: density of traffic and people, ambient events, radio with real licensed-free music, recorded engine/skid/crash sounds, car damage deformation.
P2 systems: police escalation with physics chasers and PIT/boxing, real-phone testing at 30 FPS, Tamil text (TextMeshPro or UI Toolkit with a Tamil font), save/cloud.

## 7. Build order (each step compiled and tested before the next)
1. Vehicle data and upgrade system: 8 cars, tiers, 3 stages, caps, prices, level gates, garage UI (code only, no new art needed) - can start now.
2. Real car models: replace the 8 meshes (source in section 8), wheel/rim swap per upgrade stage, body kits, paint.
3. Characters: base rigs, outfits, animation set, driver and passenger seating; then ambient crowd.
4. City art pass: building kits, landmarks, props, night lighting, wet roads.
5. Map: radar, pause map, in-world GPS.
6. Traffic/police upgrades: use the new car models, physics chasers.
7. Audio: recorded or high-quality synthesised engines, music, voices; Tamil text.
8. Device profiling: 30 FPS target on a real Android phone.

## 8. Where realistic assets can come from (licence must be checked each time)
- CC0 (no credit needed): Kenney.nl, Quaternius, Poly Pizza (filter CC0), OpenGameArt (check per item).
- CC-BY (credit required): many Sketchfab and Poly Pizza models; record author and link in ASSET_LICENCES.md.
- Paid: Unity Asset Store or CGTrader packs (low-poly game-ready car packs ~ USD 20-80 each) - fastest route to a consistent look.
- Custom: Blender modelling (a person or studio) - best match to your 8 named cars and 66 characters.
Free realistic car models are scarce and usually need a login to download, which I cannot do for you. Pick a source and a style (realistic vs stylised) and I will integrate, rig and tune them.

## Progress log
- 2026-11-17: 8 distinct procedural car bodies built (`CarShapes.cs` parameters, `CarKit.cs` generator, `CarModels.cs` builder; prefabs in `Resources/CarBodies/Body_<id>.prefab`). `VehicleCatalog` now lists the 8 cars with tiers/prices/level gates; `VehicleTuning.SwapBody` swaps the body model at runtime and `VehicleLights.Rescan` re-finds the lamps. COMPILED and checked in showroom renders (`Bridge/showroom_<id>_a/b/c.png`); NOT yet play-tested in the city. Still stylised low-poly, not realistic: real models are still needed (section 8). Upgrades (3 stages + caps) and characters/city kits are NOT done yet.
- 2026-11-17 (later): DONE and seen in Editor Play captures (not on a phone): default view is the chase camera (reset the saved top-down pref); new pedals (GAS chevrons green, BRAKE stop red, REV U-turn amber); 3-stage upgrade system with caps (GarageService.MaxUpgradeLevel = 3, tables in VehicleTuning, prices = % of car price, level gates); visible body kits Kit1..Kit3 + lowered stance per upgrade stage; vehicle showroom (8 cars, live 3D preview, stats, buy/select) and garage (3D preview, paint swatches, upgrade pips with effect text); character system (rig + 54 presets in Resources/Characters, customizer + animator, character screen with live 3D preview, saved in SaveData.driverLook); 3D city map (baked 576 roads + 3,378 buildings, pan/zoom/rotate/tilt, route, destination, tap-to-navigate, toggle with 2D). Found and fixed: SwapBody left destroyed lamp renderers in VehicleLights (now DestroyImmediate + Rescan + null guard); MonoBehaviours must live in files named like the class.
- STILL TO DO: characters are not yet placed in the world (passengers at pickups, officers, gang, pedestrians still use the old procedural figures); police/gang vehicles still use the old box car meshes; car bodies and characters are stylised low-poly, not realistic; real-phone testing; recorded audio; Tamil text.
- Tooling lesson: do not write Bridge/stop_play.txt when Unity is not playing (the next Play run then fails to spawn the car).
- 2026-11-17 (evening): police cars (white body, blue livery, roof light bar that flashes red/blue, bull bar), gang SUV and 4 traffic shapes are baked into single meshes (`TrafficCarBaker.BakeAll` -> Resources/TrafficCars/TrafficCarSet) and now used by traffic (`CityPopulation.ApplyCarLook`) and pursuit cars (`PursuitManager.SetLook`); the old box meshes remain as a fallback. Women: `CharacterLook.female` builds a female body (waist/bust/hips, slimmer limbs), hair styles 6-8 (long wavy, ponytail, bob), eyes (white, iris, pupil), lips, lashes, skirts; 16 roster presets are women (passengers, police, gang, rival, commissioner_f, driver_f); uniforms have collars, pockets, epaulettes. Seen in editor renders and a short play capture; traffic cars near the player and women in the world were NOT yet checked in Play. Still stylised low-poly, NOT photoreal.
- 2026-11-17 (night): TESTS: CoreLogicTests 56/56, VehicleTests 47/47, PlayTests 174/174 (updated for the 3-stage upgrade numbers and 140,000 supercar price). Mission passengers are now the new character models (waving, then talking at drop-off). WEB: `WebGLBuilder.Build` -> Builds/WebGL (37 MB, gzip fallback); verified in headless Chrome (loads, PLAY works, car drives, no errors). Web audio is a lighter main-thread set (no ambience/radio/thunder). Preview/map primitives use `StageMaterial` because the built-in primitive material is stripped from web builds (it showed magenta). The Editor's active build target is now WebGL (switch back to Android before building the APK).
- 2026-11-17 (late): CONTROLS now follow the Dr.-Driving-style reference: steering wheel (default steering mode = Wheel), metal ridged brake and gas pedals, P R N D gear lever (R + gas reverses, P holds the handbrake, N coasts); the REV button is gone. ANTI-FLICKER: URP Low/Medium/High now use MSAA 2/2/4 (Low render scale 0.85, Medium 1.0), camera near plane 0.3 + FXAA, chunk streaming slices at Low priority. PlayTests 174/174 after updating the reverse test. Builds refreshed: Builds/WebGL, Builds/Android/CITY_DRIVER_UNDERCOVER.apk (debug) and ..._release.apk (debug-signed, not uploadable). RULE: rebuild WebGL and Android after every change (WebGLBuilder.Build, AndroidBuilder.BuildDebugApk, PlayStorePrep.BuildReleaseApk; each switches the active build target).
- 2026-11-17 (first minute + UX polish): FIRST IMPRESSION. Old flow was a text-only studio card, a 16 s unskippable flyover with a default-font gold title, and no tutorial. Now: Karthi Lab logo card (slow zoom) -> cinematic flyover with a redesigned lockup (new bevelled gold badge with steering wheel, gold-gradient Bebas Neue "CITY DRIVER", skewed red "UNDERCOVER" ribbon, tagline, gold glint sweep) -> slim segmented loading bar with %/status/tips -> TAP TO START; TAP TO SKIP during the flyover. First launch then shows a 5-page skippable HOW TO PLAY (also in the menu). Unity splash screen is disabled and the WebGL page uses a custom loader template (`Assets/WebGLTemplates/CityDriver`: skyline, badge, title, progress, tips) instead of the default Unity logo page. Fonts: Chakra Petch (UI) and Bebas Neue (titles), SIL OFL, in `Resources/Fonts` (licence text alongside). `UiTheme.cs` holds fonts, palette and motion components (button press feedback, modal pop-in, staggered entrances, gradient text, letter spacing, slider, switch). Modals have a gold frame, header band and animation. SETTINGS is rebuilt with CONTROLS / AUDIO / GAMEPLAY / GRAPHICS tabs, drag sliders, ON/OFF switches and segmented selectors.
- 2026-11-17 (maps): police, gang cars, roadblock cars and stopped (EMP) chasers show on the round radar (flashing red/blue, orange, yellow diamond, grey; chasers beyond the radar are pinned to the rim as arrows; range rings, scanning sweep, red alert ring and a POLICE/GANG + distance label), on the flat city map and as tall pins on the 3D map, with a CHASERS legend (`ThreatMarkers.cs`, `PursuitManager.GetThreats`).
- Lesson: this project blends UI in linear space, so a translucent panel over a bright layer (even 2%) turns dark navy grey-brown: keep modal panels and frames fully opaque. Editor capture: `IntroPreview.Begin` / `BeginUi` take real Game-view screenshots (Bridge/intro_*.png).
- 2026-11-17 (driving, world and HUD pass): REFERENCE: the user's "Car Simulator: Driving & Drift" HUD image (radar + turn banner top-left, coin / level / XP / fuel / health glass panel top-right, camera button and PRND capsule on the right above the pedals, speedo arc bottom-centre, wheel bottom-left). HUD is re-laid out to match with cyan glass accents (`HudCyan`, `Rim`), the gear lever moved to the right edge, handbrake and horn beside the lever, RADIO / SETTINGS HUD buttons removed (menu and gear still reach them). SKY: the player no longer picks time or weather (HUD buttons and settings rows removed, read-only chip instead); `WorldEnvironment` starts at the device's time of day, each normal job start moves the clock on one part of the day and rolls the weather, job end usually clears it, Shadow Runs force night + rain and end at dawn. RAIN SOUND: replaced the 1.4-9.5 kHz tone stack (a hiss) with warm low-passed noise plus soft drops, volume 0.55 -> 0.3, softer thunder. SHADOW RUN FAILURE: `CareerService.DemoteAfterShadowFailure` drops one level, resets XP to the start of the lower level, clears that level's completions (it must be replayed) and removes the Shadow Pass if it falls below level 3; the result screen shows a red LEVEL DOWN banner. ATTACKERS: `ThreatHud` puts a reticle with name and distance on every visible chaser, edge arrows for off-screen ones, a red danger glow when one is within 32 m, and a CHASED banner (heat, nearest distance); radar and maps already show them. DRIVING: each of the 8 cars now has its own character (`VehicleSpec` -> `VehicleStats` "Handling character": drive layout RWD/FWD/AWD, rear grip ratio, drift grip drop, counter-steer help, steering response and curve, power oversteer, sustain, max drift angle), a drift model in `VehicleController` (slip angle, handbrake and power-slide intent, counter-steer, spin limiter, drift score banked on the HUD) and `DriftFx` (rear tyre smoke + skid marks). The steering wheel is now grabbed and turned relative to the finger (it keeps tracking outside the wheel, springs back faster the more it is turned). Showroom shows RWD/FWD/AWD and a DRIVE FEEL line. TESTS: VehicleTests section 15 compares the 8 cars (edit-mode physics, so it checks character and recovery, not feel): CoreLogic 56/56, Vehicle all passed, Play 175/175. NOT VERIFIED: real touch feel on a phone, the drift effect visuals in Play (smoke and skid marks were not captured), tuning numbers are a first pass and need hand-driving.
- 2026-11-18 (night lights + chaser visibility): ROOT CAUSE of "police / attacker cars not visible": `TrafficCarSet` cached its police / gang / paint material arrays and a stale EMPTY cache survived in the editor (arrays of length 0 -> the car mesh drew with no material). The caches are now rebuilt whenever their length differs from `baseMaterials`; `Editor/PursuerCheck.cs` (bridge command `PursuerCheck.Run`, writes Bridge/pursuer_check.txt) verifies meshes and the 11 material slots; PlayTests now check that a chaser has a mesh + material, carries its halos and is inside the camera frustum 30 m ahead. NIGHT LIGHTS: `VehicleLights` rewritten: spot lights 150 -> 320 intensity, range x1.8, 72 deg; halos on head and tail lamps (`GlowKit` / `GlowBillboard`: camera-facing soft glows that grow with distance), a warm light pool on the road ahead, brighter emissive lamps, tail lamps glow at night and flare when braking. Chasers get `PursuerGlow`: flashing red/blue roof halos + a coloured point light, headlight halos and a night beam, red tail halos, dull red underglow for gang cars. NOT VERIFIED: how it looks on a phone GPU (additional lights per pixel, limit 4 per object).
- 2026-11-18 (logo, intro, HUD alignment, Shadow Pass): NEW LOGO: rounded-hexagon badge with a gold rim that draws itself and an italic "CD" monogram with a slash cut (`IntroSequence.EmblemSprite`, layers rim / face; also the Android launcher icon and the WebGL loader SVG). INTRO: centred lockup, per-letter flying-in gold title with a travelling glint, UNDERCOVER widening between gold lines, light streak, rays, sparkles. HUD: compact status card (coins + wallet, level badge whose ring is the XP, fuel / health) with no XP text; route card, mission strip and chase banner stacked in one left-aligned column right of the radar; subtitles moved to a dialogue card in the free top zone (never over the speedometer); off-screen chaser arrows kept clear of the HUD; Shadow Pass is now a small glowing eye-card icon next to the gear (was a big red bar) and Shadow Run mission rows look like magic cards. SHADOW PASS CARD: face-down card rises in a violet aura with rays and a rune ring, flips, shockwave rings, rising embers, orbiting sparkles, edge glow, shimmer sweep and gold glint on the title, then is pulled into the camera as night falls (`MagicCardArt`, `ShadowPassSequence`). Tests: Core 56/56, Vehicle all passed, Play 177/177 (one flaky reverse-gear touch check passed on rerun).
- 2026-11-18 (city limit, city look, Play Store): CITY LIMIT: `CityBoundary` builds a solid, visible barrier (concrete base + red band + dark fence) 28 m beyond the last road on all four sides, warns the player ("City limit ahead - turn back") and puts the car back inside if it is ever found outside (tested). CITY LOOK: `CityLook.Apply` added a window-only emission map to the four building materials, a richer asphalt texture (cracks, patches, wheel tracks), livelier foliage and brighter paint; `CityMaterialFx` lights the windows at night, adds a little lane-paint glow and makes the asphalt glossy and darker in rain. SHADOW PASS: the eye sigil is replaced by a crescent moon and star (card and HUD icon). "Night" jobs now always run at night. PLAY STORE: `PlayStoreWindow` (Editor window: create upload keystore with Unity's own JDK, build signed AAB; signing pipeline proven with a throw-away key and jarsigner "jar verified", then deleted), `PlayStorePrep.ScrubCredentials`, new store graphics, `PLAY_STORE_STEP_BY_STEP.md`, `MISSION_AND_RELEASE_ANALYSIS.md`. Tests: Core 56/56, Vehicle all passed, Play 180/180.
