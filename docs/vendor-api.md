# Vendor API, contract version 1

Street Food's public API for another mod that brings its own customers to the player's carts and trucks. Persistent City is the first consumer (its E6 plan, section 3.2, is the agreement; its slice E6b binds this). This document is the Street Food side of that contract.

Status: implemented in Core with offline tests (`tests/Vendor/`). The Unity glue (`CartSelling.cs`, `VendorHost.cs`, `FoodTrucksCity.cs`) is not compiled or checked in game yet: it needs the Mac Unity build and a rig session. **Pending.**

## Without a consumer nothing changes

Until another mod calls `RequestOwner` or `Renew`, Street Food writes nothing new to the save: `SaveState.Vendor` stays null and is left out of the JSON, so the save is byte-identical to 0.10.11's. Every opening keeps Street Food's own customers. The per-tick cost is one null check and one flag check. `Read` and `Revision` never create saved state.

## Binding

| Item | Value |
| --- | --- |
| Static class | `FoodTrucks.ModApi.VendorApi` |
| Assembly | `FoodTrucks.Core` (shipped as `FoodTrucks.Core.dll` next to the mod DLL), not the `FoodTrucks` mod assembly |
| DTOs and enum | `FoodTrucks.ModApi.VendorApiVehicle`, `VendorApiSale`, `VendorApiStatus`, all in the same assembly |
| Thread | Main thread only |

Find the type by full name across loaded assemblies, or take the DTO types from `VendorApi`'s method signatures. Check `ContractVersion == 1` and every member below before using any of them. `tests/Vendor/VendorApiTests.cs` pins each name, type and parameter order. Nothing listed here is renamed or changes type while `ContractVersion` is 1. Later versions may add members.

```csharp
namespace FoodTrucks.ModApi
{
    public enum VendorApiStatus { Ready = 0, Off = 1, NotYet = 2, Refused = 3, Failed = 4 }

    public static class VendorApi
    {
        public static int ContractVersion => 1;
        public static long Revision { get; }
        public static VendorApiStatus Read(VendorApiVehicle[] into, out int count);
        public static VendorApiStatus RequestOwner(string owner, string vehicleId, bool want, out string reason);
        public static VendorApiStatus Renew(string owner, int nativeDay, int nativeMinute, out int leaseUntilDay, out int leaseUntilMinute);
        public static VendorApiStatus ReleaseAll(string owner);
        public static VendorApiStatus Sell(string owner, string vehicleId, long openingId, string item, long priceCents,
            int nativeDay, int nativeMinute, out VendorApiSale? sale, out bool uncertain, out string reason);
    }
}
```

The fields of `VendorApiVehicle` and `VendorApiSale` are public readonly fields, as listed in the E6 plan section 3.2, in that order.

## Time

Every call takes or returns the native day (from 1) and minute of the day (0-1439). Inside Street Food the absolute minute is `(day - 1) * 1440 + minute`. Neither mod's absolute minute crosses the API.

## Statuses

| Status | Meaning |
| --- | --- |
| `Ready` | Done. |
| `Off` | Street Food is not running, its data for this save is read-only (written by a newer Street Food, or unreadable), it was uninstalled in this session, or its sales tick stopped after an error. |
| `NotYet` | No save is loaded, or its data tables are not read yet. Try again later. |
| `Refused` | The call was invalid or its checks failed. Nothing was changed. `reason` says why, where the member has one. |
| `Failed` | An exception. For `Sell`, read `uncertain`. |

The save may hold vendor data written by a newer Street Food (block `version` above 1). Then every owner call is `Refused` and that data is kept unchanged.

## Owners

An owner name is 1-64 characters from `a-z`, `0-9`, `.`, `_` and `-`, for example `persistentcity`. At most 8 owners hold leases, and each owner can request at most 64 vehicles. A vehicle has at most one requesting owner. When an owner is at 64 requests, Street Food first drops its requests for vehicles the player no longer owns. The owner should still call `RequestOwner(owner, id, false)` for a vehicle that disappears from `Read`.

## Read and Revision

- `Revision` changes when any field of any vehicle could have changed. Compare it for equality only. A newly loaded save always starts at a new value. While nothing is attached, `Revision` is 0. Reading `Revision` marks the API as in use, so Street Food keeps the snapshot fresh from then on.
- `Read` copies the vehicles into `into` and sets `count`. It does not allocate. While `Revision` is unchanged it returns the same `VendorApiVehicle` instances, so treat their arrays as read-only. More than 64 vehicles, or an array shorter than the vehicle count, returns `Refused` with count 0, never a truncated list.
- Street Food refreshes the copy on each sales tick (every 0.25 s real time) while the API is in use, and immediately after each owner call. After a sale it rebuilds only that vehicle's copy. Between ticks a `Read` can be up to one tick old. `Sell` checks live state.
- The list contains every food vehicle the player owns, including closed ones, in ordinal ID order.

Field notes, where version 1 says more than the plan:

| Field | Version 1 behaviour |
| --- | --- |
| `OpeningId` | Assigned when the window opens, once an owner has used the API in this save. An opening that began before then has ID 0 and can never be granted. IDs rise with each opening and with the clock, and are saved. |
| `OpeningOwner` | `""` while closed. |
| `OpenedDay`, `OpenedMinute` | -1 while closed or when `OpeningId` is 0. |
| `ClosesDay`, `ClosesMinute` | The end of the crew route's open leg for this stop. -1 when the player opened the window, or when no planned leg covers it. |
| `X`, `Y`, `Z` | The customer side of the window. On a truck: 2.0 m out from the right-hand hatch, 1.10 m behind the middle. On a cart: the counter on its queue side. Ground height is the vehicle's. PC checks the NavMesh itself. |
| `QueueX`, `QueueY`, `QueueZ`, `QueueHeading` | The first waiting place. A truck's line runs towards its back, 1.0 m apart. A cart's line runs straight out from the window, 0.8 m apart. The heading is Unity yaw in degrees. |
| `MealsPerHour` | `floor(60 / orderMinutes * speed * servers)`. `orderMinutes` is Street Food's equal-weight service time over the items in stock, and `speed` and `servers` are the window's current team, including the player when they serve at it. 0 while closed or while nobody serves. |
| `PriceCents` | The price Street Food would charge now (custom price, then district pricing manager, then default). 0 for an item that has no market price, and `Sell` refuses that item. A closed vehicle has no district, so its price may change when it opens. |
| `Units` | Paid, unsealed stock, minus units held for Street Food's own customers on their way and units reserved for a catering gig. |
| `EventOrCatering` | The vehicle holds a place at a city event, or a catering gig with it has started. |

## Ownership

Defaults Peter accepted in E6 section 9:

- At an opening an owner holds, the owner's customers **replace** Street Food's own.
- Only openings a crew starts can be granted. An opening the player starts and works stays with Street Food. If the player joins a crew opening, it stays the owner's.
- City-event and catering openings stay with Street Food.
- Street Food's reputation and health grades do not change in either case.

The rules:

1. `RequestOwner(owner, id, true)` records the request with the current native hour. A repeated request keeps the first hour. It is refused for a vehicle that is not one of the player's food vehicles (`no such food vehicle`) and for a vehicle another owner already requested (`requested by another owner`). The first `RequestOwner` or `Renew` creates the save's vendor block.
2. A window opens. The opening is granted to the requesting owner only if all of these hold: a crew opened it; it is not at a city event or catering gig; it started in a later native hour than the request; and the owner's lease holds at the opening minute. Otherwise the opening is Street Food's and the request stays for the next opening. An open opening never moves from Street Food to an owner. For a requested vehicle, each decision is logged (`Vendor API: ... granted to ...` or `... stays with Street Food (...)`).
3. While an owner holds an opening, Street Food generates **none of its own customers** for it. Everything else continues: closing at the stop's end, sell-outs, licences, events, crew pay for serving, open minutes for regulars, health inspections and midnight takings.
4. Street Food takes an opening back, logs it, and starts its own customers from that minute with a fresh queue when any of these happens:
   - `RequestOwner(owner, id, false)` is called.
   - `ReleaseAll(owner)` is called.
   - The lease runs out.
   - A city event or catering gig takes the vehicle.
   Street Food saves the hand-back minute with the opening. A crew route's sales that are still catching up to the clock get no Street Food customers for the minutes before it, because the owner still held those minutes.
5. Closing ends the ownership. The next opening is decided again under rule 2.

## Lease

- `Renew(owner, day, minute)` sets the lease to end 60 native minutes after the given minute. It never shortens a lease. The given minute may not be ahead of Street Food's clock, and may lag by less than 60 minutes. Anything else is `Refused`. It returns the lease end as native day and minute.
- A lease that has run out is removed, and its openings are handed back at the minute it ran out. Street Food checks this on every sales minute of an owned window, on each tick, and before `Renew` and `Sell`. The lease end is saved. A save loaded without the owner mod therefore hands every vehicle back within 60 game minutes. The requests stay, so the owner is granted the next opening after it renews.
- `ReleaseAll(owner)` drops the owner's lease and all its requests, and hands back all its openings at once.

## Sell

`Sell` runs these checks in order and refuses at the first one that fails, before anything is touched. `reason` takes the value in brackets.

1. Valid owner, vehicle ID, item, a positive price and a valid time (`invalid ...`).
2. The minute is Street Food's current native minute: not after it (`minute not reached yet`) and not before it (`not the current minute`). A sale is never booked for a past minute, so ownership, lease, stock and price are all checked at the minute now.
3. The vehicle is open (`vehicle closed`) and its current opening is `openingId` (`opening changed`).
4. The owner holds this opening at that minute, with the lease holding (`not the owner of this opening`). A hand-back that is due happens first.
5. The minute is not before the opening started (`minute before the opening`), and not at or after the planned close (`minute after the planned close`).
6. Somebody serves the window (`nobody serving`), and the menu has at most 8 items (`menu overflow`).
7. The item is on the menu (`item not on the menu`) and `priceCents` equals the current price (`price changed`).
8. At least one sellable unit is left (`sold out`).
9. The sales ledger and the midnight takings can record the sale (`the street food record is full`).

If every check passes, Street Food makes its own one-item sale at that minute: `CartSelling.SellItem` as a crew sale. It is booked in the day's ledger and the midnight takings, the unit is taken from cargo, the crew batch is flushed (one cargo update, the hour's crew log line), and the result is saved. It returns `Ready` and a `VendorApiSale`:

- `BookedDay`: the native day of the sale minute. The player receives the money in Street Food's midnight payment for that day and vehicle, together with Street Food's own sales.
- `Cents`: the cents taken. They equal `priceCents`.
- `UnitsBefore`, `UnitsAfter`: the item's sellable units before and after the sale. `UnitsAfter` is -1 if the units could not be read after the sale.
- `Sequence`: Street Food's counter of API sales for this vehicle. It is saved and goes up by one with each sale.

When the sale itself fails:

| Result | When | What to do |
| --- | --- | --- |
| `Failed`, `uncertain = true`, `sale = null` | An exception, or a refusal, came after the ledger record. The sale is booked. | Treat it as made. Never retry it. |
| `Failed`, `uncertain = false` | An exception came before the ledger record. Nothing was sold. | Nothing was booked. |
| `Refused`, reason `sale refused` | `SellItem` refused before any effect, for example when the unit vanished between the check and the sale. | Nothing was booked. |

## Save

The block is `vendor` in Street Food's mod data (`foodtrucks:state`). It has its own layout `version` (1) and is additive. Older Street Food versions keep it unchanged as an unknown field. It holds:

- the leases: owner and end minute;
- the requests: owner, vehicle and hour;
- per vehicle: last and current opening ID, the opening minute, the opening's owner, the minute it was handed back (if it was) and the API sale counter.

Loading rules:

- The block is read apart from the rest, so a bad block never makes the save unreadable. A block that fails validation is dropped, with a log line, and every vehicle stays with Street Food.
- A block with a newer layout version is kept exactly as written, and the API refuses owner calls.
- An owner left on a closed vehicle is cleared on load.

Street Food's own `schemaVersion` does not change.

## Code

| Part | File |
| --- | --- |
| Public contract | `Core/ModApi/VendorApi.cs` |
| Checks, snapshot and revision | `Core/Vendor/VendorService.cs` |
| Grant, lease, hand-back and save rules | `Core/Vendor/VendorBook.cs` |
| Window position and serving rate | `Core/Vendor/VendorGeometry.cs` |
| Saved block | `Core/Vendor/VendorState.cs` |
| Game side | `unity/FoodTrucks/Scripts/VendorHost.cs`; the hooks in `CartSelling.cs` (opening, close, no own customers, `VendorSellOne`, `VendorFactsOf`); attach and detach in `FoodTrucksCity.cs` |

## Not yet checked

These need the Mac Unity build and a rig session. Code reading is not a check.

- The Unity glue compiles against the game DLLs.
- A granted crew opening shows no Street Food queue and no Street Food sales.
- A hand-back starts Street Food's customers again.
- An API sale appears in the day summary and the midnight payment.
- The window and queue positions match the visible line.
- A save with and without the vendor block loads in game.
- A crew window whose sales lag the clock gets no Street Food customers before a hand-back. This is checked in Core only; `CartSelling.Tick` asks `VendorBook.OwnCustomers` for each step's minute.
- Performance, from the E6a row: the Street Food tick with 0, 8 and 64 open vehicles, owned and with Street Food's own customers; the cost and allocations of `Read`, of the snapshot refresh while the API is in use, and of `Sell`.

E6 plan section 7 lists the batched checks (contract present, missing and old; ownership at an opening; exact money; hand-back; lease without PC; player-opened windows; uncertain sale). They also need a consumer build, or a test double on the bridge.
