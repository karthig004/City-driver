# Expected vs actual (against the two reference screenshots)

Reference A = the blue-car racing HUD (distance/time, round minimap, speedometer arc, pedal pads).
Reference B = the yellow-taxi game (mission panel, radio, stat bars, dialogue, dense city).
Status legend: MATCH = same idea and similar look, PARTIAL = built but looks/places differently, GAP = not there.
"Tested" means an automated Editor Play check or a Game-view screenshot; nothing here has been checked on a phone.

## HUD and layout
| Expected (reference) | Actual now | Status |
|---|---|---|
| A: DISTANCE (km) + TIME at the top-left | During a mission: "DISTANCE 2.0 km / TIME 00:13 LEFT 05:08" top-left, right of the minimap | MATCH |
| A: round minimap with "N" and a ring | Round masked minimap top-left with a gold ring and an N that turns with the map | MATCH (smaller icons: no P / POI badges) |
| A: speedometer arc 0-7 with gear and big km/h | Arc with 0-7 ticks, red zone, rpm needle, gear, big speed | MATCH in idea; fonts/finish simpler |
| A: coin counter + gear icon top-right | Wallet / level / XP / fuel / health panel top-right (Rs, no coin icon); Settings is a button in the left column | PARTIAL |
| A: right-side icon buttons (camera, traction, lights, nitro) | Right-edge stack: MAP, CAM, HAND BRAKE, HORN as text buttons; no icons, no nitro / traction control | PARTIAL |
| A: chevron steering arrows, grip-pad brake/gas | Triangle arrows, plain rounded pedals; steering wheel + tilt as alternatives | PARTIAL |
| B: mission title + objective panel top-left | Banner "Take <name> to <place>" at top centre | PARTIAL |
| B: time-of-day icons top-right | Four quick buttons MORN / DAY / DUSK / NIGHT | MATCH in idea (text, not icons) |
| B: fuel / engine / condition bars | Fuel and Health bars with km of range | PARTIAL (no separate engine bar) |
| B: radio widget with frequency | RADIO button cycling 3 stations (+ Settings) | PARTIAL (no frequency dial) |
| B: dialogue boxes with character portraits and reply choices | Subtitle bar + optional device text-to-speech voice | GAP (no portraits, no choices) |
| B: passenger patience bar, fare meter | Not implemented (fare is paid at the end) | GAP |

## Gameplay
| Expected | Actual now | Status |
|---|---|---|
| First view: a real street with traffic and buildings, car low in frame | The player now spawns on a street in the left lane facing the city centre; chase camera 3.1 m high, 7.4 m back, looking down the road | MATCH in framing; the city is still low-poly |
| Long trips ("short trips irritate") | Trips are planned on the road network: about 1.3 km (Level 1) up to 3.4 km (Long Journey), x0.7 / x1 / x1.5 by the Trip Length setting; longer trips pay a distance bonus; pickup is 60-320 m away | NEW, test written (see status) |
| Fuel so long trips matter | 1% per 150 m, refuel at petrol stations / garage, tow when empty, Fuel Tank upgrade extends range | NEW, test written |
| Night / rain / fog, headlights | Presets + weather + auto headlights | NEW |
| Police / gang chase, heat, roadblocks | Implemented for Shadow Run | NEW |

## Visual realism (the biggest gap)
| Expected | Actual now | Status |
|---|---|---|
| Photoreal city: wet reflective asphalt, dense signage, shop fronts, lighting depth, post-processing | Low-poly procedural buildings with CC0 facade textures, fog and sky presets, rain particles; no wet-road reflections, no bloom/colour grading | GAP (large) |
| Realistic pedestrians (A/B) | Procedural low-poly people: rounded limbs, painted faces, hair/turban/cap variants, saree/kurta/school outfits, walk cycle | PARTIAL (still stylised; far from photoreal) |
| Realistic trees | Layered individual-leaf textures, fissured bark texture, more leaf density per card; the canopy mesh is unchanged (22-26 cards per tree) | PARTIAL (better; still not photoreal) |
| Realistic traffic cars / player car | Box-style traffic cars; lofted procedural player sedan | GAP / PARTIAL |
| Street life (props) | Pooled bins, benches, planters, hawker stalls with awnings, motorbikes, sign poles, bus shelters along sidewalks | NEW |

## Why the realism gap remains
Photoreal characters, cars and buildings need professionally modelled, textured and rigged assets. Free ones that are legally usable
are scarce and usually need an account to download (Sketchfab, Mixamo, Quixel). The project only uses assets that are free and
downloadable without a login (ambientCG CC0 textures). If you can supply licensed models (FBX/GLB with textures), the pedestrian,
vehicle and tree slots are the places to swap them in; I can wire them up.
