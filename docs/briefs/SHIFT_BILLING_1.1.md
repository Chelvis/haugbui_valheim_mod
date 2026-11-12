# Shift billing — version 1.1.0 (not chosen)

v1 charges the full shift cost once, when the Haugbui starts working during that shift, even if little time is
left. HB-179 keeps that until 1.1.0. This file is the choice the 1.1 session must put to Kelvin before writing code.
Do not pick an option in M13.

## How v1 works

A day has two shifts, from world time (`WorldClock`):

- Day shift: raw fraction 0.15 to 0.85 (70% of the day).
- Night shift: raw fraction 0.85 to 1 and 0 to 0.15 (30% of the day). The hours after midnight belong to the night
  that started the evening before.

Without the Altar he may be out only during the night, and he starts home `DawnReturnLeadSeconds` (default 60 real
seconds) before dawn. With the Altar he may be out all day and all night. Each of those windows is its own shift.

The cost depends on the tomb level: 5, 5, 10, 10, 15, 15. It is charged once per shift, and only if there is work
and the balance covers it. Starting halfway through the shift still costs the whole amount. There is no refund.

He can start in the middle because Rest was turned off, tribute was offered late, or work appeared after the shift
had already begun. A new tomb waits for the next dusk, so its first night does start on the boundary. With the
Altar, a return from death can also be in the middle of the day.

## Option 1 — only start on a boundary

Without the Altar he leaves the tomb only at dusk, never during the night. A Rest toggle cleared at midnight waits
until the next dusk. The charge is still the full night cost, and he gets the night.

With the Altar he may leave at any moment. The shift is no longer the day/night boundary. It is half of the current
game-day length, starting when he leaves. If the day is 20 real minutes, "shift ends in" counts 10 minutes from
that departure, whatever the sun is doing. The next shift, if he is still out, starts when that half day ends. The
charge for each of those half days is the full shift cost.

## Option 2 — pay for the time left

He may still leave in the middle of a shift.

If that moment is the shift boundary (dusk without the Altar; dusk or dawn with the Altar), charge the full cost.

Otherwise charge `floor(cost * seconds left / full shift length)`. A shift that costs 10 and lasts 10 minutes, with
4 minutes left, costs 4. A remainder below 1 costs 0, and he does not emerge for a free fragment: he waits for the
next boundary.

With the Altar the same rule applies to the day shift and the night shift. The "shift ends in" clock stays on the
day/night boundary.
