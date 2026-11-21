# Promo video, YouTube text and store copy

## The three films (all have on-screen text, spoken lines and subtitles)
Both are recorded from the real game. Files are in `Bridge/trailer_tools/`:
- **`CITY_DRIVER_UNDERCOVER_trailer_classic.avi`** - the first version (53.5 s, 1,605 frames, 288 MB): "ONE CITY." / "A THOUSAND SECRETS." / "EVERY RIDE HAS A PASSENGER" / "EVERY RIDE HAS A SECRET" / "SHADOW RUN" on-screen text at the top, dialogue subtitles at the bottom, title card at the end. Rebuilt from the same shot list after the original files were overwritten (same shots, same grade; not byte-identical).
- **`CITY_DRIVER_UNDERCOVER_trailer_cinematic.avi`** - the new cut (65.3 s, 1,960 frames, 357 MB): about 35 shots, ACES + teal/orange grade, day/night flicker, split title ("CITY DRIVER" in daylight, "UNDERCOVER" in the dark), dialogue subtitles.
- **`CITY_DRIVER_UNDERCOVER_overview.avi`** - the overview (77.6 s, 2,328 frames, 524 MB). Clean, peaceful and classical in normal mode: soft morning-to-day light, slow camera moves, elegant letter-spaced callouts on the real features (A LIVING CITY / REAL ROADS. REAL ROUTES. / TURN-BY-TURN GPS / LONG JOURNEYS ACROSS TOWN / TEN LEVELS OF CAREER / SIX VEHICLE CLASSES / UPGRADE. EARN. DRIVE. / DAY AND NIGHT. SUN AND RAIN.), a calm narrator, and a piano-and-strings score (D major, flute line, bells, birds). Then a black beat of silence and the **Shadow Pass** as a thriller: night rain, police lights, heartbeat and ticking, staccato strings, a tremolo cluster, taiko, sirens, a radio voice, quick cuts and slow motion. Peace returns for the title card.
All three: 1920x1080, 30 fps, 2.39:1 letterbox, Motion-JPEG video + 16-bit stereo audio. Windows' media player opens them (duration, resolution, audio+video checked). YouTube accepts AVI uploads.

**Dialogue** (`dialogue_classic.json`, `dialogue_cinematic.json`): short lines between a driver (male voice), a passenger (female voice) and a police radio (UK female voice with a radio filter). Spoken with the built-in Windows speech voices (Microsoft David / Zira / Hazel), so they sound synthetic, not like recorded actors. The same JSON drives the audio and the subtitles. Edit the text or times, run `generate_vo.ps1`, then re-record and re-mix.
Nothing in the dialogue or the text states the driver's double life: it is only implied by the pictures and by lines such as "Who are you?" / "Just a driver."

Other files: `trailer_score_*.wav` (music + voices), `Bridge/trailer/` (cinematic frames), `Bridge/trailer_classic/` (classic frames), `Bridge/store_assets/` (feature graphic + 6 screenshots).

Rebuild (Unity menu **City Driver > Promo**): "Record trailer frames" (cinematic), "Record classic trailer frames" or "Record overview trailer frames" (overview: score with `python make_score3.py trailer_timeline_overview.json out.wav`), then
`powershell -File generate_vo.ps1 dialogue_X.json vo_X`, `set VO_JSON=...dialogue_X.json`, `set VO_DIR=...vo_X`, `python make_score2.py <timeline.json> out.wav` (cinematic) or `python make_score.py out.wav` (classic), then `python -I make_trailer.py <frames_dir> out.wav out.avi`.

## How the film tells the story (no words about it)
One car, one city. A warm, calm morning-to-day (aerial crane into the street, a pass-by, tracking shots, a quiet plucked score and birds) turns at dusk into a cold, wet night: the same street and the same framing, but with a heartbeat that quickens, a huge hit on the cut, police lights in the rain and a driving percussion score. Near the end the two worlds flicker against each other on the same framing, faster and faster, and the title splits the same way: **CITY DRIVER** appears in daylight, **UNDERCOVER** appears in the dark. On-screen words in the cinematic cut: the studio card, the dialogue subtitles, the title and "COMING SOON - ANDROID".

(Cinematic cut) Look: ACES tonemapping, teal / orange split toning, bloom, depth of field, motion blur, film grain, chromatic aberration, slow motion, flashing police lights with glow, a moon fill light at night, letterbox. Rendered offline at 1080p on the high quality level.

## Honest limits
- The picture is still the game's low-poly city and cars. Lighting and editing lift it a lot, but it is not photoreal or "film-budget". Licensed 3D models for the car, people and buildings are the biggest upgrade left.
- The pursuit cars are held in a controlled convoy behind the player (real gang cars ram and flip the car, which looked wrong), so the chase is staged rather than fully live AI.
- The score is synthesised in code: it is original and cinematic in structure, but it will not sound like a recorded orchestra.
- The AVI is Motion-JPEG because ffmpeg could not be installed here. Re-encode to MP4 (Clipchamp / HandBrake) if you want a smaller file before uploading.
- The end card says "COMING SOON - ANDROID". Change it once the game is actually live.

## YouTube upload text
**Title:** CITY DRIVER: UNDERCOVER - Official Teaser (Android)

**Description:**
One city. One car. A thousand secrets.

CITY DRIVER: UNDERCOVER is an offline Android driving game: a large road-network city with traffic and pedestrians, long trips across town, changing light and rain, a ten-level career and night missions where police and gang cars close in.

Coming soon to Android. Follow for updates.

#CityDriver #Undercover #AndroidGames #DrivingGame #IndieGame #Trailer

**Tags:** city driver, undercover, android game, driving game, taxi game, open world driving, indie game, game trailer, teaser

**Thumbnail:** `Bridge/store_assets/screenshot_5_shadow_run.png` (police convoy) or `screenshot_6_logo.png`.

## Store copy
**Short description (<= 80 chars):** Drive an open city by day, run Shadow missions by night. Offline, no ads.

**Full description:**
Take the wheel of CITY DRIVER: UNDERCOVER.

- DRIVE A LIVING CITY - a large road-network city with traffic, pedestrians, parks and towers, in morning, day, sunset and night, with rain and fog.
- LONG TRIPS, REAL ROUTES - pick up passengers and take them across town with turn-by-turn GPS, a minimap and a full map. Longer trips pay a distance bonus; watch your fuel.
- SHADOW RUN - night missions bring police and gang chases, roadblocks and escapes.
- 10-LEVEL CAREER - earn money and XP, unlock vehicles in six classes, upgrade engine, brakes, armour and fuel tank, and buy defensive gadgets.
- DESIGNED FOR PHONES - buttons, steering wheel or tilt steering, adjustable camera views.
- OFFLINE - no ads, no purchases, no account.

(Only list features that are in the version you upload.)
