using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Demand;

namespace FoodTrucks.Core.Selling
{
    public sealed class StoragePool
    {
        internal StoragePool(EquipmentInfo piece, int pieces, int capacity, double load, IReadOnlyDictionary<string, int> units)
        {
            Piece = piece;
            Pieces = pieces;
            Capacity = capacity;
            Load = load;
            Units = units;
        }

        public EquipmentInfo Piece { get; }
        public string EquipmentId => Piece.Id;
        public int Pieces { get; }
        public int Capacity { get; }
        public double Load { get; }
        public IReadOnlyDictionary<string, int> Units { get; }
        public double Room => Math.Max(0, Capacity - Load);
        public int LoadUnits => (int)Math.Min(int.MaxValue, Math.Floor(Load + 1e-9));
    }

    public sealed class StorageView
    {
        internal StorageView(VehicleInfo vehicle, IReadOnlyList<StoragePool> pools, int storagePieces, double factor,
            IReadOnlyDictionary<string, int> unheld)
        {
            Vehicle = vehicle;
            Pools = pools;
            StoragePieces = storagePieces;
            Factor = factor;
            Unheld = unheld;
        }

        public VehicleInfo Vehicle { get; }
        public IReadOnlyList<StoragePool> Pools { get; }
        public int StoragePieces { get; }
        public double Factor { get; }
        public IReadOnlyDictionary<string, int> Unheld { get; }
        public int PerPiece => Storage.Capacity(Vehicle.UnitsPerPiece, 1, Factor);

        public StoragePool? PoolFor(DemandTables tables, string? itemName)
        {
            var piece = tables.EquipmentForProduct(itemName);
            return piece == null ? null : Pools.FirstOrDefault(p => p.EquipmentId == piece.Id);
        }
    }

    public enum LoadRefusal
    {
        None,
        NoPiece,
        PieceFull,
        EquipmentSlots,
        SecondStorage
    }

    public readonly struct LoadCheck
    {
        public LoadCheck(int allowed, LoadRefusal refusal, string? equipmentId = null, int capacity = 0)
        {
            Allowed = allowed;
            Refusal = refusal;
            EquipmentId = equipmentId;
            Capacity = capacity;
        }

        public int Allowed { get; }
        public LoadRefusal Refusal { get; }
        public string? EquipmentId { get; }
        public int Capacity { get; }
    }

    public sealed class RestockPlan
    {
        internal RestockPlan(IReadOnlyDictionary<string, int> load, IReadOnlyList<string> notHeld)
        {
            Load = load;
            NotHeld = notHeld;
        }

        public IReadOnlyDictionary<string, int> Load { get; }
        public IReadOnlyList<string> NotHeld { get; }
    }

    public static class Storage
    {
        public static int Capacity(int unitsPerPiece, int pieces, double factor) =>
            (int)Math.Min(int.MaxValue, Math.Floor((double)unitsPerPiece * Math.Max(0, pieces) * factor + 1e-9));

        public static StorageView? Of(DemandTables tables, string? vehicleType, IEnumerable<CargoLine> cargo)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (cargo == null) throw new ArgumentNullException(nameof(cargo));
            var vehicle = tables.Vehicle(vehicleType);
            if (vehicle == null) return null;
            var lines = cargo.ToList();
            var pieces = new List<(EquipmentInfo Piece, int Count)>();
            var storagePieces = 0;
            var factor = 1.0;
            foreach (var id in CargoRules.FittedEquipment(tables, lines))
            {
                var piece = tables.Equipment(id)!;
                if (piece.IsStorage)
                {
                    storagePieces++;
                    factor = Math.Max(factor, piece.StorageFactor);
                    continue;
                }
                var index = pieces.FindIndex(p => p.Piece.Id == piece.Id);
                if (index < 0) pieces.Add((piece, 1));
                else pieces[index] = (piece, pieces[index].Count + 1);
            }

            var units = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var line in lines)
            {
                if (line.HasNestedCargo || line.Amount <= 0 || line.ItemName == null || tables.Product(line.ItemName) == null) continue;
                units[line.ItemName] = (units.TryGetValue(line.ItemName, out var u) ? u : 0) + line.Amount;
            }

            var pools = new List<StoragePool>();
            foreach (var (piece, count) in pieces)
            {
                var held = new Dictionary<string, int>(StringComparer.Ordinal);
                var load = 0.0;
                foreach (var product in piece.Products)
                {
                    if (!units.TryGetValue(product, out var n)) continue;
                    held[product] = (int)Math.Min(int.MaxValue, n);
                    load += n * tables.Product(product)!.StockWeight;
                }
                pools.Add(new StoragePool(piece, count, Capacity(vehicle.UnitsPerPiece, count, factor), load, held));
            }
            var unheld = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var pair in units)
            {
                var piece = tables.EquipmentForProduct(pair.Key);
                if (piece == null || pools.All(p => p.EquipmentId != piece.Id)) unheld[pair.Key] = (int)Math.Min(int.MaxValue, pair.Value);
            }
            return new StorageView(vehicle, pools, storagePieces, factor, unheld);
        }

        public static LoadCheck Check(DemandTables tables, string? vehicleType, IEnumerable<CargoLine> cargo,
            string? incomingItem, int incomingAmount, bool incomingHasNestedCargo)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (cargo == null) throw new ArgumentNullException(nameof(cargo));
            var amount = Math.Max(0, incomingAmount);
            var lines = cargo.ToList();
            var vehicle = tables.Vehicle(vehicleType);
            if (amount == 0 || vehicle == null) return new LoadCheck(amount, LoadRefusal.None);

            var piece = tables.EquipmentForGameItem(incomingItem);
            if (piece != null)
            {
                if (piece.IsStorage && CargoRules.FittedEquipment(tables, lines).Any(id => tables.Equipment(id)!.IsStorage))
                    return new LoadCheck(0, LoadRefusal.SecondStorage);
                var coolerCount = lines.Where(l => EquipmentLayout.IsCooler(tables.EquipmentForGameItem(l.ItemName)?.Id))
                    .Sum(l => (long)Math.Max(0, l.Amount));
                var used = EquipmentPieces(tables, lines) - (int)Math.Min(vehicle.CoolerSlots, coolerCount);
                var reserved = EquipmentLayout.IsCooler(piece.Id)
                    ? Math.Max(0, vehicle.CoolerSlots - (int)Math.Min(vehicle.CoolerSlots, coolerCount)) : 0;
                var allowed = used > vehicle.EquipmentSlots ? 0 : Math.Min(amount, vehicle.EquipmentSlots - used + reserved);
                if (allowed == 0) return new LoadCheck(0, LoadRefusal.EquipmentSlots);
                if (piece.IsStorage && amount > 1) return new LoadCheck(1, LoadRefusal.SecondStorage);
                return new LoadCheck(allowed, allowed < amount ? LoadRefusal.EquipmentSlots : LoadRefusal.None);
            }

            if (incomingHasNestedCargo) return new LoadCheck(amount, LoadRefusal.None);
            var product = tables.Product(incomingItem);
            if (product == null) return new LoadCheck(amount, LoadRefusal.None);
            var view = Of(tables, vehicleType, lines)!;
            var pool = view.PoolFor(tables, incomingItem);
            if (pool == null) return new LoadCheck(0, LoadRefusal.NoPiece, tables.EquipmentForProduct(incomingItem)?.Id);
            var fits = StockLimit.CanLoad(pool.Load, amount, pool.Capacity, product.StockWeight);
            return new LoadCheck(fits, fits < amount ? LoadRefusal.PieceFull : LoadRefusal.None, pool.EquipmentId, pool.Capacity);
        }

        public static RestockPlan Restock(DemandTables tables, string? vehicleType, IEnumerable<CargoLine> cargo,
            IReadOnlyDictionary<string, int> available)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (cargo == null) throw new ArgumentNullException(nameof(cargo));
            if (available == null) throw new ArgumentNullException(nameof(available));
            var load = new Dictionary<string, int>(StringComparer.Ordinal);
            var notHeld = new List<string>();
            var view = Of(tables, vehicleType, cargo);
            foreach (var pair in available.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (pair.Value <= 0 || tables.EquipmentForGameItem(pair.Key) != null) continue;
                if (view == null || view.PoolFor(tables, pair.Key) == null) notHeld.Add(pair.Key);
            }
            if (view == null) return new RestockPlan(load, notHeld);

            foreach (var pool in view.Pools)
            {
                var foods = pool.Piece.Products
                    .Where(p => available.TryGetValue(p, out var n) && n > 0)
                    .Select(p => new Food(p, tables.Product(p)!.StockWeight, available[p],
                        (pool.Units.TryGetValue(p, out var u) ? u : 0) * tables.Product(p)!.StockWeight))
                    .ToList();
                if (foods.Count == 0 || pool.Load >= pool.Capacity) continue;
                Fill(foods, pool.Load, pool.Capacity);
                foreach (var food in foods)
                    if (food.Planned > 0) load[food.Item] = food.Planned;
            }
            return new RestockPlan(load, notHeld);
        }

        public static IReadOnlyDictionary<string, int> FitPieces(DemandTables tables, string? vehicleType, IEnumerable<CargoLine> cargo,
            IReadOnlyDictionary<string, int> available)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (cargo == null) throw new ArgumentNullException(nameof(cargo));
            if (available == null) throw new ArgumentNullException(nameof(available));
            var fit = new Dictionary<string, int>(StringComparer.Ordinal);
            if (tables.Vehicle(vehicleType) == null) return fit;
            var lines = cargo.ToList();
            foreach (var pair in available.OrderBy(p => tables.EquipmentForGameItem(p.Key)?.IsStorage == true ? 0 : 1)
                         .ThenBy(p => p.Key, StringComparer.Ordinal))
            {
                if (pair.Value <= 0 || tables.EquipmentForGameItem(pair.Key) == null) continue;
                var allowed = Check(tables, vehicleType, lines, pair.Key, pair.Value, false).Allowed;
                if (allowed <= 0) continue;
                fit[pair.Key] = allowed;
                lines.Add(new CargoLine(pair.Key, allowed, false));
            }
            return fit;
        }

        // Whole food items of each demand category the vehicle can hold, from the pieces fitted now: each pool's room
        // in items of its lightest food of that category, counted by the loader's own rule (StockLimit.CanLoad, so no
        // advertised item fails to load), the pools serving one category added up. Categories no fitted piece serves
        // are left out.
        // The lightest food makes this an upper bound for a shared piece whose foods of one category weigh differently.
        // Shipped data has one such piece only with the Bakery mod: the display case holds 160 croissants on a truck
        // but 80 bread. Kept on purpose: a gig takes any food of its category and the player picks the stock, so the
        // heaviest weight would hide gigs a lighter load can serve; a truck short of food at the gig is NotReady, and
        // the player can restock and come back before the deadline.
        public static IReadOnlyDictionary<string, int> CategoryCapacity(DemandTables tables, string? vehicleType, IEnumerable<CargoLine> cargo)
        {
            var byCategory = new Dictionary<string, long>(StringComparer.Ordinal);
            var view = Of(tables, vehicleType, cargo);
            if (view != null)
                foreach (var pool in view.Pools)
                    foreach (var group in pool.Piece.Products.Select(p => tables.Product(p)!).GroupBy(p => p.Category, StringComparer.Ordinal))
                    {
                        long items = StockLimit.CanLoad(0, int.MaxValue, pool.Capacity, group.Min(p => p.StockWeight));
                        byCategory[group.Key] = Math.Min(int.MaxValue, (byCategory.TryGetValue(group.Key, out var c) ? c : 0) + items);
                    }
            return byCategory.ToDictionary(p => p.Key, p => (int)p.Value, StringComparer.Ordinal);
        }

        private static int EquipmentPieces(DemandTables tables, IEnumerable<CargoLine> cargo)
        {
            var units = 0L;
            foreach (var line in cargo)
                if (tables.EquipmentForGameItem(line.ItemName) != null && line.Amount > 0) units += line.Amount;
            return (int)Math.Min(int.MaxValue, units);
        }

        private sealed class Food
        {
            public Food(string item, double weight, int available, double current)
            {
                Item = item;
                Weight = weight;
                Available = available;
                Current = current;
            }

            public string Item { get; }
            public double Weight { get; }
            public int Available { get; }
            public double Current { get; }
            public int Planned { get; set; }
            public double Level => Current + Planned * Weight;
            public bool CanTakeMore => Planned < Available;
        }

        private static void Fill(List<Food> foods, double poolLoad, int capacity)
        {
            var otherLoad = poolLoad - foods.Sum(f => f.Current);
            double Total() => otherLoad + foods.Sum(f => f.Level);
            int UnitsAt(Food f, double level) =>
                (int)Math.Max(0, Math.Min(f.Available, Math.Floor((level - f.Current) / f.Weight + 1e-9)));

            double low = 0, high = capacity + foods.Max(f => f.Current) + 1;
            for (var i = 0; i < 100; i++)
            {
                var middle = (low + high) / 2;
                var total = otherLoad;
                foreach (var f in foods) total += f.Current + UnitsAt(f, middle) * f.Weight;
                if (total <= capacity) low = middle;
                else high = middle;
            }
            foreach (var f in foods) f.Planned = UnitsAt(f, low);

            while (true)
            {
                var room = capacity - Total();
                var next = foods.Where(f => f.CanTakeMore && f.Weight <= room + 1e-9 && Total() + f.Weight <= capacity)
                    .OrderBy(f => f.Level).FirstOrDefault();
                if (next == null) break;
                next.Planned++;
            }
        }
    }
}
