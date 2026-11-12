# Task schedules — version 1.2.0

After v1.0. Not M13. v1 still plans one chore at a time. HB-182.

Today the planner picks the single best next chore. A feed trip is one station, so two charcoal kilns beside one
chest become chest, kiln, chest, kiln. The bag also unloads at 80% of its weight, and again every 3 real minutes
after an emptying finishes (`CarryCycle.ForcedUnloadSeconds` is 180, not 3 seconds). Either rule can leave him
holding items that belong to a chore he has not started.

## Schedules

He builds one schedule, carries it out, then looks again. Kinds, in the default order:

1. **Delivery.** Open Delivery Chest lines.
2. **Production.** Stations waiting for input or fuel.
3. **Collection.** Collection Chests, then ground items, into storage chests.

That order is a list in data, not a chain of `if`s, so a later setting can reorder the three. Combat and "empty
the tomb storage on emerge" are fixed and are not part of the list.

At the end of a schedule he chooses again. A finished collection does not continue if a delivery appeared. He
does not switch kinds in the middle.

**Emerge.** The first chore is emptying tomb storage (HB-044). Then a schedule.

**Enter the tomb** (dawn, Rest, death, unload). The bag goes into tomb storage, and the schedule is discarded.

**Combat.** A parallel queue with absolute priority. He pauses, fights, and resumes the same schedule at the
next stop. Stops the player already satisfied are skipped. He does not build a new schedule until that one ends.

## Delivery

Sum the weight of everything the open lines still need, from ground and from chests.

- Heavier than the bag: fill the bag with requested items, deliver them, repeat.
- Lighter than the bag: take all of them, then fill the free capacity with ground items that have a storage
  destination, calculated before he picks anything up. Deliver the requests. Store the surplus. The schedule ends.

## Production

Same shape: stations that are actually waiting, ground and Collection Chests before storage chests, collect the
batch, then feed every station in the batch. Repeat while stations still clear the cutoff. If the unmet demand
is under the cutoff, skip production and try collection.

**Cutoff, to confirm in the 1.2 session.** Recommendation: start a production schedule only when the unmet
inputs weigh at least the greater of 20 and 20% of carry capacity, and a station that is already over half full
does not count as waiting. A flat 20 is 10 wood. At level 1 the bag holds 25 wood, so 20 weight is a real floor.
At level 6 the bag holds 75 wood, and 20% is 30 weight, so a trickle of one or two logs does not start a new
walk. Two kilns that each want 25 wood are 100 weight and always qualify, and they share one trip when the bag
can hold both loads.

## Collection

Collection Chests first, then ground items that have a destination, then the storage chests. One batch, then the
schedule ends and he looks at delivery again.

## Route

A perfect shortest path over the whole base, every frame, is the wrong tool. One schedule has few stops. For a
trip of about ten stops, ordering them exactly is cheap. The useful rule is smaller than that: plan the stops
before he moves, and do not return to the wood chest between two kilns when the bag already holds both loads.
Nearest-remaining-stop is enough if a trip is long. Collect-everything-then-deliver-everything is the right
shape when the pickups are in one place and the dropoffs are in another. When two dropoffs are neighbors, the
route visits both before any new pickup.

## What does not change

Item safety, the storage rule, keep-amount, leave-one, territories, wards, and the night and Altar windows.
The 3-minute clock and the 80% rule are removed, because the bag is emptied at the end of the schedule. Leftovers
from a station the player filled himself are stored then, not on a timer.
