#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FoodTrucks
{
    // Real Life Overhaul's running job (a Gig Line gig or a rota shift), read by reflection so the bridge works without
    // RLO: what the shift card says, where the game's guider points, a gig's client building and a street cart's
    // vendor spot [RLO 0.40.1: Jobs/JobDirector.cs, Jobs/Job.cs, Jobs/JobSpots.cs DoorSpot, Kinds/CleanerJob.cs,
    // Kinds/VendorJob.cs, Jobs/JobStand.cs]. Null when RLO isn't loaded or no job runs.
    internal static class TestBridgeJob
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static JToken? State()
        {
            var director = AccessTools.TypeByName("RealLifeOverhaul.JobDirector");
            var instance = director?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
            if (instance == null) return null;
            var job = director!.GetField("_job", Any)?.GetValue(instance);
            if (job == null) return null;
            var type = job.GetType();
            var def = Get(job, "Def");
            var result = new JObject
            {
                ["id"] = Get(def, "Id") as string,
                ["kind"] = type.Name,
                ["objective"] = Get(job, "Objective") as string,
                ["over"] = Get(job, "Over") as bool? ?? false,
            };
            if (Get(job, "HasMarker") is bool marker && marker && Get(job, "MarkerAt") is Vector3 at)
                result["marker"] = new JObject { ["x"] = Math.Round(at.x, 1), ["z"] = Math.Round(at.z, 1), ["label"] = Get(job, "MarkerLabel") as string };

            // A gig at a client's building (cleaning): the building to walk to and go into.
            var client = type.GetField("_client", Any)?.GetValue(job);
            if (client != null && Get(client, "Building") is CityBuildingController building && building != null)
            {
                var address = building.building?.Address;
                var front = Get(client, "Front") is Vector3 f ? f : building.transform.position;
                result["client"] = new JObject
                {
                    ["address"] = address == null ? null : World.AddressKey(address.streetName, address.streetNumber),
                    ["name"] = Get(client, "Name") as string,
                    ["business"] = building.buildingRegistration?.BusinessName,
                    ["x"] = Math.Round(front.x, 1),
                    ["z"] = Math.Round(front.z, 1),
                };
                if (type.GetField("_endsAt", Any)?.GetValue(job) is float ends)
                    result["client"]!["seconds_left"] = Math.Max(0, Math.Round(ends - Time.time));
            }

            // A street cart (Uncle Lou's): the vendor's spot behind it, which starts the work when the player walks on.
            // Until RLO's solid check has passed (_pitchReady) the spot is only a candidate: RLO shows no marker yet, and
            // a player standing on it can make the next candidates fail, so it is reported as being set up.
            var gigCart = type.GetField("_pitchReady", Any) != null;
            if (gigCart && !(type.GetField("_pitchReady", Any)!.GetValue(job) as bool? ?? false)
                && type.GetField("_post", Any)?.GetValue(job) is Vector3 pending && pending != Vector3.zero)
            {
                result["cart"] = new JObject { ["setting_up"] = true };
            }
            else if (type.GetField("_post", Any)?.GetValue(job) is Vector3 post && post != Vector3.zero)
            {
                var cart = type.GetField("_cartAt", Any)?.GetValue(job) is Vector3 c ? c : post;
                var stand = Get(type.GetField("Ctx", Any)?.GetValue(job), "Stand");
                result["cart"] = new JObject
                {
                    ["post_x"] = Math.Round(post.x, 1),
                    ["post_z"] = Math.Round(post.z, 1),
                    ["cart_x"] = Math.Round(cart.x, 1),
                    ["cart_z"] = Math.Round(cart.z, 1),
                    ["standing"] = Get(stand, "Standing") as bool? ?? false,
                    ["opened"] = type.GetField("_open", Any)?.GetValue(job) as bool? ?? false,
                };
            }
            return result;
        }

        // ---- Simulated play ----

        // What a competent player earns against the clock's base pay for the same units, by job kind: the skip pays
        // 85% of the base (ShiftRates.Share) without tips or bonuses; a good hand-played shift earns the full base plus
        // the whole-bundle bonus (flyers, shelves), quick-service tips and the no-walk-off bonus (the cart), or the
        // tips a well-played song draws (busking) [RLO 0.40.1: CleanerJob.cs:501, StockerJob.cs:492, FlyerJob.cs:384,
        // VendorJob.Serving.cs:66 and VendorJob.cs:303, BuskerJob.cs:390].
        private static readonly (string Type, double Rate)[] Competent =
        {
            ("CleanerJob", 1.00), ("StockerJob", 1.10), ("FlyerJob", 1.10), ("VendorJob", 1.50), ("BuskerJob", 1.33),
        };
        private const double SkipShare = 0.85;
        private static readonly string[] Driven = { "CourierJob", "TaxiJob" };
        private static double? armedRate;
        private static bool patched;

        // job auto: the running job's minigame played for the player as a competent player would, as a time skip: the
        // job's own "Let time pass" started and run on the game's time machine, then paid up from its 85% to the
        // competent rate. Only the kinds with that skip; driving jobs are driven.
        internal static JToken Run(JObject args)
        {
            var op = TestBridgeCommands.Need(args, "op", "auto");
            if (op != "auto") throw new BridgeError($"Unknown job op '{op}': auto.");
            var director = AccessTools.TypeByName("RealLifeOverhaul.JobDirector")
                           ?? throw new BridgeError("Real Life Overhaul isn't loaded: no jobs to play.");
            var instance = director.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
            var job = instance == null ? null : director.GetField("_job", Any)?.GetValue(instance);
            if (job == null) throw new BridgeError("No job is running: take a gig or clock on first.");
            var type = job.GetType();
            if (Array.IndexOf(Driven, type.Name) >= 0) return SimulateDriving(director, instance!, job, type);
            var rate = Array.Find(Competent, c => c.Type == type.Name).Rate;
            if (rate == 0) throw new BridgeError($"{type.Name} has no time skip to simulate; play it with ui.");
            var ui = InstanceBehavior<UI.UIs>.Instance?.playerActivityUI ?? throw new BridgeError("The game's activity panel isn't there.");
            if (ui.GetCurrentActivity != null && ui.GetCurrentActivityState != PlayerActivityState.Finished)
                throw new BridgeError("An activity is already under way; let it finish or stop it first.");
            EnsurePatched();
            // The busker's skip is behind its More button.
            if (type.Name == "BuskerJob" && type.GetField("_more", Any) is FieldInfo more && !(more.GetValue(job) is bool m && m))
                type.GetMethod("More", Any, null, Type.EmptyTypes, null)?.Invoke(job, null);
            var offer = type.GetMethod("OfferSkip", Any, null, Type.EmptyTypes, null) ?? throw new BridgeError($"{type.Name} has no OfferSkip.");
            offer.Invoke(job, null);
            var skip = ui.GetCurrentActivity;
            if (skip == null || skip.GetType().Name != "ShiftSkip")
                throw new BridgeError("The job can't be skipped at this step: " + (Get(job, "Objective") as string ?? "do what the job asks first") + ".");
            armedRate = rate;
            skip.GetType().GetMethod("Start", Any, null, Type.EmptyTypes, null)?.Invoke(skip, null);
            var pump = TestBridgePump.Instance ?? throw new BridgeError("The bridge's runner is gone.");
            pump.StartCoroutine(PressTimeMachine(ui, skip));
            Log.Info($"Test bridge: job auto: {type.Name} simulated at {rate:0.00} of base pay, on the time machine.");
            return new JObject { ["kind"] = type.Name, ["rate"] = rate, ["started"] = true };
        }

        // ---- Driving jobs (courier, ride-hail) ----

        // Driving is a skill like the minigames, and the self-driving ride can't stop beside a fare or keep off
        // pedestrians, so a driving shift is played for the player as a competent driver would: RLO's own figures
        // for each parcel or fare (UnitPay, DistancePay, its trip sampler and day caps), paid through JobDirector.Pay,
        // then the clock runs for the driving time RLO's par times allow. Nothing is crashed and nobody is hit.
        private const int DrivingShiftMinutes = 300;        // a fare shift as long as a gig shift elsewhere
        private const float DrivingEnergyPerHour = 2.5f;    // as the drinks cart's clock costs (about 10 in 4 h)
        private const float EnergyFloor = 12f;              // work never takes energy below this (RLO's guide)

        private static JToken SimulateDriving(Type director, object instance, object job, Type type)
        {
            var game = SaveGameManager.Current;
            var payKey = type.Name == "TaxiJob" ? "reallife_job_taxi_slip" : "reallife_job_courier_slip";
            var slipType = AccessTools.TypeByName("RealLifeOverhaul.PaySlip") ?? throw new BridgeError("RLO's PaySlip isn't there.");
            var pay = director.GetMethod("Pay", Any, null, new[] { AccessTools.TypeByName("RealLifeOverhaul.Job"), slipType.MakeByRefType(), typeof(string) }, null)
                      ?? throw new BridgeError("RLO's JobDirector.Pay(Job, in PaySlip, string) isn't there.");
            int UnitPay() => (int)Call(job, "UnitPay")!;
            int DistancePay(float metres) => (int)Call(job, "DistancePay", metres)!;
            var tipFactor = Get(Get(job, "Ctx"), "TipFactor") as float? ?? 1f;
            int units = 0, total = 0;
            double minutes = 0;
            if (type.Name == "CourierJob")
            {
                var count = Get(job, "_count") as int? ?? 0;
                var delivered = Get(job, "_delivered") as int? ?? 0;
                var route = Get(job, "_route") as float? ?? 0f;
                var window = Get(job, "_window") as float? ?? 0f;
                if (count <= 0) throw new BridgeError("The round isn't planned yet: take the parcels job first.");
                // The rest of the round, delivered in about three quarters of the time the client allows; the doors tip
                // as often as RLO's recipients do (35%, 5.5 to 16.5 each at the job's rate).
                var rate = Get(Get(job, "Ctx"), "Rate") as float? ?? 1f;
                var left = count - delivered;
                var tips = Get(job, "_tips") as int? ?? 0;
                for (var i = 0; i < left; i++) if (UnityEngine.Random.value < 0.35f) tips += Mathf.RoundToInt(UnityEngine.Random.Range(5.5f, 16.5f) * rate);
                Set(job, "_tips", tips);
                Set(job, "_delivered", count);
                Call(job, "UnitDone", count, UnitPay());
                var before = game.Money;
                Call(job, "Settle", true);
                units = left;
                total = Mathf.RoundToInt(game.Money - before);
                minutes = window * 0.75 * (count > 0 ? (double)left / count : 1);
            }
            else
            {
                var spots = AccessTools.TypeByName("RealLifeOverhaul.JobSpots") ?? throw new BridgeError("RLO's JobSpots isn't there.");
                var spotType = AccessTools.TypeByName("RealLifeOverhaul.DoorSpot")!;
                var randomDoor = spots.GetMethods(BindingFlags.Public | BindingFlags.Static).First(m => m.Name == "RandomDoor");
                float C(string name) => Convert.ToSingle(type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetRawConstantValue() ?? throw new BridgeError($"RLO's {type.Name}.{name} isn't there."));
                var from = PlayerHelper.GetPosition();
                var leftToday = (int)(AccessTools.TypeByName("RealLifeOverhaul.JobState")?.GetMethod("LeftToday", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new[] { Get(job, "Def") }) ?? 0);
                var fares = Get(job, "_fares") as int? ?? 0;
                var energy = game.Energy;
                while (units < leftToday && minutes < DrivingShiftMinutes && energy - DrivingEnergyPerHour * (minutes / 60) > EnergyFloor + 2)
                {
                    var args1 = new object?[] { from, C("PickupMin"), C("PickupMax"), null, C("LaneWithin"), null };
                    var parameters = randomDoor.GetParameters();
                    var call1 = parameters.Select((p, i) => i < args1.Length ? args1[i] : p.DefaultValue).ToArray();
                    if (!(bool)randomDoor.Invoke(null, call1)!) break;
                    var pickup = call1[5]!;
                    var front = (Vector3)spotType.GetField("Front")!.GetValue(pickup);
                    var call2 = parameters.Select((p, i) => i switch { 0 => front, 1 => C("TripMin"), 2 => C("TripMax"), 3 => spotType.GetField("Building")!.GetValue(pickup), 4 => C("LaneWithin"), 5 => null, _ => p.DefaultValue }).ToArray();
                    if (!(bool)randomDoor.Invoke(null, call2)!) break;
                    var drop = (Vector3)spotType.GetField("Front")!.GetValue(call2[5]!);
                    var metres = Flat(front, drop);
                    var fare = UnitPay() + DistancePay(metres);
                    var slip = Activator.CreateInstance(slipType);
                    slipType.GetField("Units")!.SetValue(slip, 1);
                    slipType.GetField("Base")!.SetValue(slip, UnitPay());
                    slipType.GetField("Distance")!.SetValue(slip, DistancePay(metres));
                    // A smooth, quick ride: comfort 75 to 89 (12 to 18% tip) and the quick bonus RLO adds for it.
                    slipType.GetField("Tips")!.SetValue(slip, Mathf.RoundToInt(fare * UnityEngine.Random.Range(0.12f, 0.18f) * tipFactor));
                    slipType.GetField("Bonus")!.SetValue(slip, Mathf.RoundToInt(fare * 0.05f));
                    Call(job, "UnitDone", fares + 1, fare);
                    var before = game.Money;
                    pay.Invoke(instance, new[] { job, slip, payKey });
                    total += Mathf.RoundToInt(game.Money - before);
                    fares++;
                    Set(job, "_fares", fares);
                    units++;
                    // RLO's own times: the drive to the fare at a good pace, 30 s to get in, its par for the ride, and the pause between fares.
                    minutes += Flat(from, front) / 8f + 30 + metres / 7.5f + 20 + 8;
                    from = drop;
                }
                if (units == 0) throw new BridgeError("No fare could be found from here, or the day's fares or your energy are used up.");
            }
            Call(job, "Finish", Enum.Parse(AccessTools.TypeByName("RealLifeOverhaul.JobEnd"), "Done"));
            // The clock below runs fast, and the game drains energy then as for someone walking about; a driving shift
            // costs what the other gigs' clocks cost, so energy is set to that once the clock has run (hunger falls as usual).
            var spent = (float)(DrivingEnergyPerHour * minutes / 60);
            var startEnergy = game.Energy;
            var targetEnergy = startEnergy <= EnergyFloor ? startEnergy : Mathf.Max(EnergyFloor, startEnergy - spent);
            spent = startEnergy - targetEnergy;
            var end = game.Day * 1440.0 + game.Hour * 60 + game.Minute + Math.Max(1, minutes);
            var day = (int)(end / 1440); var minuteOfDay = (int)(end % 1440);
            TestBridgeCommands.RunClockUntil(day, minuteOfDay);
            (TestBridgePump.Instance ?? throw new BridgeError("The bridge's runner is gone.")).StartCoroutine(SetEnergyAfter(targetEnergy));
            Log.Info($"Test bridge: job auto: {type.Name} simulated: {units} unit(s), {total} paid, {minutes:F0} game minutes on the clock, energy -{spent:F1}.");
            return new JObject { ["kind"] = type.Name, ["simulated"] = true, ["units"] = units, ["paid"] = total, ["minutes"] = Math.Round(minutes), ["until"] = $"day {day} {minuteOfDay / 60:D2}:{minuteOfDay % 60:D2}" };
        }

        private static IEnumerator SetEnergyAfter(float energy)
        {
            while (TestBridgeCommands.RunningUntil) yield return null;
            var game = SaveGameManager.Current;
            if (game != null && !EnergyHelper.goingToHospital) game.Energy = energy;
        }

        private static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        private static object? Call(object target, string name, params object[] args)
        {
            var method = target.GetType().GetMethods(Any).FirstOrDefault(m => m.Name == name && m.GetParameters().Length == args.Length)
                         ?? throw new BridgeError($"RLO's {target.GetType().Name}.{name} isn't there.");
            return method.Invoke(target, args);
        }

        private static void Set(object target, string name, object value)
        {
            for (var t = target.GetType(); t != null; t = t.BaseType)
            {
                var field = t.GetField(name, Any | BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
            }
            throw new BridgeError($"RLO's {target.GetType().Name}.{name} isn't there.");
        }

        // Once the skip runs, the game's time-machine key, as the player presses it on the activity panel.
        private static IEnumerator PressTimeMachine(PlayerActivity.PlayerActivityUI ui, object skip)
        {
            var until = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < until)
            {
                if (!ReferenceEquals(ui.GetCurrentActivity, skip)) { armedRate = null; yield break; }
                if (ui.GetCurrentActivityState == PlayerActivityState.Running)
                {
                    AccessTools.Method(typeof(PlayerActivity.PlayerActivityUI), "OnTimeMachinePress")?.Invoke(ui, null);
                    yield break;
                }
                yield return null;
            }
            if (ui.GetCurrentActivityState != PlayerActivityState.Running) armedRate = null;
        }

        private static void EnsurePatched()
        {
            if (patched) return;
            var finish = AccessTools.Method(AccessTools.TypeByName("RealLifeOverhaul.ShiftSkip"), "Finish")
                         ?? throw new BridgeError("Real Life Overhaul's ShiftSkip.Finish isn't there; this RLO version isn't supported.");
            new Harmony("foodtrucks.testbridge.jobsim").Patch(finish,
                prefix: new HarmonyMethod(typeof(TestBridgeJob), nameof(FinishPrefix)),
                postfix: new HarmonyMethod(typeof(TestBridgeJob), nameof(FinishPostfix)));
            patched = true;
        }

        // Before the skip settles: the job and what its shift had paid so far.
        private static void FinishPrefix(out object?[]? __state)
        {
            __state = null;
            if (armedRate == null) return;
            var director = AccessTools.TypeByName("RealLifeOverhaul.JobDirector");
            var instance = director?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
            var job = instance == null ? null : director!.GetField("_job", Any)?.GetValue(instance);
            if (job == null) return;
            __state = new[] { instance, job, Get(Get(job, "Shift"), "Total"), Get(Get(job, "Shift"), "Units") };
        }

        // After it settles: the difference between the competent rate and the 85% the skip paid, as tips.
        private static void FinishPostfix(object?[]? __state)
        {
            var rate = armedRate;
            armedRate = null;
            if (rate == null || __state == null) return;
            try
            {
                var (instance, job) = (__state[0], __state[1]!);
                var shift = Get(job, "Shift");
                var paid = (Get(shift, "Total") as int? ?? 0) - (__state[2] as int? ?? 0);
                var units = (Get(shift, "Units") as int? ?? 0) - (__state[3] as int? ?? 0);
                var topUp = (int)Math.Round(paid / SkipShare * rate.Value) - paid;
                if (paid <= 0 || topUp <= 0) return;
                var slipType = AccessTools.TypeByName("RealLifeOverhaul.PaySlip");
                var slip = Activator.CreateInstance(slipType);
                slipType.GetField("Tips")!.SetValue(slip, topUp);
                var pay = instance!.GetType().GetMethod("Pay", Any, null, new[] { AccessTools.TypeByName("RealLifeOverhaul.Job"), slipType.MakeByRefType(), typeof(string) }, null)
                          ?? throw new InvalidOperationException("JobDirector.Pay(Job, in PaySlip, string) not found");
                pay.Invoke(instance, new object[] { job, slip!, "Simulated play" });
                Log.Info($"Test bridge: job auto: {job.GetType().Name} skip paid {paid} for {units} unit(s); topped up by {topUp} to {rate:0.00} of base.");
            }
            catch (Exception ex) { Log.Error("Test bridge: job auto top-up failed: " + ex); }
        }

        // A property or field by name, public or not; null when it isn't there.
        private static object? Get(object? target, string name)
        {
            if (target == null) return null;
            var type = target.GetType();
            var property = type.GetProperty(name, Any);
            if (property != null && property.GetIndexParameters().Length == 0) return property.GetValue(target);
            return type.GetField(name, Any)?.GetValue(target);
        }
    }
}
