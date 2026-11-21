# CITY DRIVER: UNDERCOVER - whole-game analysis before the Play Store (2026-11-18)

"Verified" = checked by the automated tests / captures on the dev PC (Unity Editor, headless Chrome, build reports). Nothing was run on a phone.

## 1. Mission system (30 missions, 10 levels x 3)
| Area | State |
|---|---|
| Structure | 10 levels x 3 assignments, upgrade gates per level, Shadow Pass at the end of Level 3, weapon shop at Level 5 (verified by CoreLogicTests 56/56) |
| Flow | pick up -> drive (GPS) -> drop off -> pay out (wallet, XP, level); timers, damage deduction, safe-driving bonus, replay pays 25% (verified) |
| Shadow Runs (levels 4-10, 21 missions) | night + rain, gang / police chase, roadblocks, smoke + EMP, escape or get busted. **Failing now drops you one level** (XP reset to the lower level, its assignments must be replayed, LEVEL DOWN banner) |
| Dynamic world | time and weather are not chosen: they move on when jobs start and clear when they end; "night" jobs always run at night |

**Honest gaps found in the review** (design intent vs what is built):
1. All 30 missions use the same three pickup / drop-off spots (pickup_a/b/c, dropoff_a/b/c) and the same single-passenger loop. They differ by reward, timer and pursuit plan, not by a unique scenario. "Multiple Passengers", "Double Rescue", "Double Passenger Rescue" still carry ONE passenger.
2. "VIP Transport" (no hard braking) uses the normal safe-driving bonus; it is not a separate rule.
3. Mission names like "Nightclub Extraction" / "Secret Warehouse" are story text; the locations are the generic markers. Cutscenes exist only for the Shadow Pass.
4. Economy is a first pass: Level 1 pays 300-600 Rs, the first extra car costs 7,000 Rs (about 14 jobs); Level 10 pays up to 13,000. Tune after real playtests.
5. Replay value now comes from the 8 cars (each drives differently), drifting, weather and Shadow Run difficulty rather than mission variety.
Recommended next content work (not blocking the store): distinct pickup / drop-off sets per mission, true two-passenger missions, a VIP comfort meter, and 3-5 story cutscenes.

## 2. What is in the game (verified unless noted)
- City: 1.92 km road-graph city streamed in 97 chunks; **solid visible barrier round the edge (city limit warning, car is put back if it ever leaves)**; richer asphalt, livelier trees, **lit windows at night, wet reflective roads in rain**.
- Vehicles: 8 cars, 3 tiers, 3 upgrade stages each with visible body kits; each car has its own handling (RWD / FWD / AWD, loose or planted, drift character); drift model with smoke, skid marks and a drift score.
- Controls: relative-grab steering wheel, metal pedals, P-R-N-D lever, brake-into-turn drifting, keyboard on desktop / web.
- World: dynamic night / rain / fog, police and gang chasers with highlighted tracking (markers, edge arrows, CHASED banner, radar and map blips), traffic, pedestrians.
- Interface: new "CD" logo and animated intro, first-run How To Play, tabbed Settings, glass HUD, magical Shadow Pass card.
- Builds: WebGL (about 38 MB), Android APK (debug-signed test build), Android release APK, release AAB workflow (needs YOUR upload keystore).
- Tests: CoreLogic 56/56, Vehicle all passed, Play 180/180 (touch controls, missions, chases, weather, UI, streaming, boundary, intro).

## 3. Not done / risks (read before publishing)
| Risk | Why it matters | What to do |
|---|---|---|
| **Never run on a real phone** | Touch feel, frame rate (30 FPS target), heat, memory, audio and tilt are only judged in the Editor | Install the release APK on a mid-range phone and play 15 min (guide step 3) |
| Art is stylised low-poly (procedural) | Not photoreal; the store page must not promise real cars/brands | Use honest screenshots; licensed models are the biggest future upgrade |
| Audio is synthesised | No recorded music / voices; Tamil text and voice are not implemented | Acceptable for a first release; list English only |
| Android text-to-speech hook unverified | Voice lines may be silent on some phones | Subtitles are on by default |
| Mission variety (section 1) | Players may feel repetition after Level 2-3 | Ship as early access / v0.1 and extend |
| Debug-signed files | `CITY_DRIVER_UNDERCOVER.apk` and `*_release.apk` are signed with the Android debug key | Only upload the AAB made in the Play Store window with YOUR keystore |
| New developer account rules | Personal accounts need a 14-day closed test with 12+ testers before production | Start recruiting testers now |

## 4. Verdict
The game is technically complete for a first closed test: it builds as a release bundle, is offline and permission-free, and every automated check passes. Publish to a **closed test first**, collect phone feedback (performance, controls, balance), then go to production. See `PLAY_STORE_STEP_BY_STEP.md`.
