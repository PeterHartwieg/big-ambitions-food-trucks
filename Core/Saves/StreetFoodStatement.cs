using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Saves
{
    public sealed class StatementVehicle
    {
        public StatementVehicle(string vehicleId, int columns)
        {
            VehicleId = vehicleId;
            Total = new long[columns];
        }

        public string VehicleId { get; }
        public long[] Total { get; }
        public SortedDictionary<LedgerLine, long[]> Lines { get; } = new SortedDictionary<LedgerLine, long[]>();
    }

    public sealed class StreetFoodStatement
    {
        public const long ToleranceCents = 5;

        private StreetFoodStatement(int columns)
        {
            Total = new long[columns];
            Licence = new long[columns];
            EventPlaces = new long[columns];
            DeliveryFees = new long[columns];
            NotBrokenDown = new long[columns];
            MovedWages = new long[columns];
            MovedParking = new long[columns];
        }

        public long[] Total { get; }
        public List<StatementVehicle> Vehicles { get; } = new List<StatementVehicle>();
        public long[] Licence { get; }
        public long[] EventPlaces { get; }
        public long[] DeliveryFees { get; }
        public long[] NotBrokenDown { get; }
        public long[] MovedWages { get; }
        public long[] MovedParking { get; }

        public bool IsEmpty =>
            Total.All(v => v == 0) && Vehicles.Count == 0 && Licence.All(v => v == 0) &&
            EventPlaces.All(v => v == 0) && DeliveryFees.All(v => v == 0) && NotBrokenDown.All(v => v == 0);

        public static StreetFoodStatement Build(SaveState? state, IReadOnlyList<int> days,
            IReadOnlyList<long> gameWagesCents, IReadOnlyList<long> gameParkingCents)
        {
            if (days == null) throw new ArgumentNullException(nameof(days));
            if (gameWagesCents == null || gameWagesCents.Count != days.Count) throw new ArgumentException("One wage figure per day.", nameof(gameWagesCents));
            if (gameParkingCents == null || gameParkingCents.Count != days.Count) throw new ArgumentException("One parking figure per day.", nameof(gameParkingCents));

            var columns = days.Count;
            var statement = new StreetFoodStatement(columns);
            if (state == null) return statement;
            var vehicles = new SortedDictionary<string, StatementVehicle>(StringComparer.Ordinal);

            for (var c = 0; c < columns; c++)
            {
                var entry = state.DailySales.FirstOrDefault(d => d != null && d.Day == days[c] && d.InSummary);
                if (entry == null) continue;
                var byVehicle = (entry.ByVehicle ?? new List<VehicleDay>()).Where(v => v != null).ToList();

                var wages = byVehicle.Aggregate(0L, (sum, v) => checked(sum + v.WagesCents));
                var parking = byVehicle.Aggregate(0L, (sum, v) => checked(sum + v.ParkingCents));
                var moveWages = wages > 0 && gameWagesCents[c] > 0 && wages <= gameWagesCents[c] + ToleranceCents;
                var moveParking = parking > 0 && gameParkingCents[c] > 0 && parking <= gameParkingCents[c] + ToleranceCents;
                if (moveWages) statement.MovedWages[c] = wages;
                if (moveParking) statement.MovedParking[c] = parking;

                long itemised = 0;
                foreach (var day in byVehicle)
                {
                    if (!vehicles.TryGetValue(day.VehicleId, out var vehicle))
                        vehicles[day.VehicleId] = vehicle = new StatementVehicle(day.VehicleId, columns);
                    itemised = checked(itemised + day.SalesCents + day.CateringCents - day.StockCents - day.PermitCents - day.CommissaryRentCents - day.OvernightFineCents - day.MarketingCents);
                    Put(vehicle, LedgerLine.Sales, c, day.SalesCents);
                    Put(vehicle, LedgerLine.Catering, c, day.CateringCents);
                    Put(vehicle, LedgerLine.Stock, c, -day.StockCents);
                    if (moveWages) Put(vehicle, LedgerLine.Wages, c, -day.WagesCents);
                    if (moveParking) Put(vehicle, LedgerLine.Parking, c, -day.ParkingCents);
                    Put(vehicle, LedgerLine.Permit, c, -day.PermitCents);
                    Put(vehicle, LedgerLine.CommissaryRent, c, -day.CommissaryRentCents);
                    Put(vehicle, LedgerLine.OvernightFine, c, -day.OvernightFineCents);
                    Put(vehicle, LedgerLine.Marketing, c, -day.MarketingCents);
                }

                statement.Licence[c] = -entry.LicenceCents;
                statement.EventPlaces[c] = -entry.EventPlacesCents;
                statement.DeliveryFees[c] = -entry.DeliveryFeesCents;
                itemised = checked(itemised - entry.LicenceCents - entry.EventPlacesCents - entry.DeliveryFeesCents);
                statement.NotBrokenDown[c] = checked(SalesLedger.SummaryCents(state, days[c]) - itemised);
            }

            foreach (var vehicle in vehicles.Values.Where(v => v.Lines.Count > 0))
            {
                for (var c = 0; c < columns; c++)
                    vehicle.Total[c] = vehicle.Lines.Values.Aggregate(0L, (sum, line) => checked(sum + line[c]));
                statement.Vehicles.Add(vehicle);
            }
            for (var c = 0; c < columns; c++)
            {
                var total = checked(statement.Licence[c] + statement.EventPlaces[c] + statement.DeliveryFees[c] + statement.NotBrokenDown[c]);
                foreach (var vehicle in statement.Vehicles) total = checked(total + vehicle.Total[c]);
                statement.Total[c] = total;
            }
            return statement;
        }

        private static void Put(StatementVehicle vehicle, LedgerLine line, int column, long cents)
        {
            if (cents == 0) return;
            if (!vehicle.Lines.TryGetValue(line, out var values))
                vehicle.Lines[line] = values = new long[vehicle.Total.Length];
            values[column] = checked(values[column] + cents);
        }
    }
}
