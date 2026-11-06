using System;
using FoodTrucks.Core.Vendor;

// Street Food's public vendor API, contract version 1 (docs/vendor-api.md). Another mod binds these members by name
// through reflection; nothing here may be renamed or change type while ContractVersion is 1. Main thread only.
namespace FoodTrucks.ModApi
{
    public enum VendorApiStatus { Ready = 0, Off = 1, NotYet = 2, Refused = 3, Failed = 4 }

    // One food vehicle as Street Food sees it. A detached, immutable copy: the same instance is handed out again
    // while VendorApi.Revision is unchanged, so its arrays must be treated as read-only.
    public sealed class VendorApiVehicle
    {
        // Street Food's vehicle ID, 1-128 UTF-16 units.
        public readonly string VehicleId;
        // "cart" or "truck".
        public readonly string Kind;
        public readonly bool Open;
        // Increases on every opening of this vehicle and is never reused. 0 for an opening that began before any
        // owner first used the API in this save (it is never granted).
        public readonly long OpeningId;
        // "" (Street Food) or the owner that holds this opening.
        public readonly string OpeningOwner;
        // False when the player opened it.
        public readonly bool OpenedByCrew;
        // The opening holds a place at a city event or the vehicle is at a catering gig.
        public readonly bool EventOrCatering;
        // Native day and minute of day; -1 when closed or not known (OpeningId 0).
        public readonly int OpenedDay, OpenedMinute;
        // The planned close of a crew opening; -1 when there is none (player-opened, or closed).
        public readonly int ClosesDay, ClosesMinute;
        // The customer side of the window (world position).
        public readonly float X, Y, Z;
        // Where the first waiting customer stands, and the heading (degrees, Unity yaw) the line runs towards.
        public readonly float QueueX, QueueY, QueueZ, QueueHeading;
        // Street Food's current serving rate, floor; 0 = nobody serving.
        public readonly int MealsPerHour;
        // More than 8 menu items; the arrays below are then empty.
        public readonly bool MenuOverflow;
        // The equipment menu, ordinal order, exact item names.
        public readonly string[] Items;
        // This vehicle's current price; 0 for an item with no market price (Sell refuses it).
        public readonly long[] PriceCents;
        // Sellable units (paid, unsealed, not held for a customer or a catering gig), >= 0.
        public readonly int[] Units;

        internal VendorApiVehicle(string vehicleId, string kind, bool open, long openingId, string openingOwner, bool openedByCrew,
            bool eventOrCatering, int openedDay, int openedMinute, int closesDay, int closesMinute,
            float x, float y, float z, float queueX, float queueY, float queueZ, float queueHeading, int mealsPerHour,
            bool menuOverflow, string[] items, long[] priceCents, int[] units)
        {
            VehicleId = vehicleId;
            Kind = kind;
            Open = open;
            OpeningId = openingId;
            OpeningOwner = openingOwner;
            OpenedByCrew = openedByCrew;
            EventOrCatering = eventOrCatering;
            OpenedDay = openedDay;
            OpenedMinute = openedMinute;
            ClosesDay = closesDay;
            ClosesMinute = closesMinute;
            X = x;
            Y = y;
            Z = z;
            QueueX = queueX;
            QueueY = queueY;
            QueueZ = queueZ;
            QueueHeading = queueHeading;
            MealsPerHour = mealsPerHour;
            MenuOverflow = menuOverflow;
            Items = items;
            PriceCents = priceCents;
            Units = units;
        }
    }

    // One sale made through VendorApi.Sell.
    public sealed class VendorApiSale
    {
        // Native day the takings are booked on (paid in at the following midnight).
        public readonly int BookedDay;
        public readonly long Cents;
        public readonly int UnitsBefore, UnitsAfter;
        // Street Food's VendorApi sale counter for this vehicle.
        public readonly long Sequence;

        internal VendorApiSale(int bookedDay, long cents, int unitsBefore, int unitsAfter, long sequence)
        {
            BookedDay = bookedDay;
            Cents = cents;
            UnitsBefore = unitsBefore;
            UnitsAfter = unitsAfter;
            Sequence = sequence;
        }
    }

    public static class VendorApi
    {
        public static int ContractVersion => 1;

        // Changes whenever any field of a vehicle above could change. Only an equality check is meaningful.
        public static long Revision => VendorRuntime.Revision();

        // Copies the vehicles into `into` (at most 64). More than 64 vehicles, or too small an array, is Refused with
        // count 0, never a truncated list.
        public static VendorApiStatus Read(VendorApiVehicle[] into, out int count)
        {
            count = 0;
            var service = VendorRuntime.Current;
            return service == null ? VendorApiStatus.Off : service.Read(into, out count);
        }

        public static VendorApiStatus RequestOwner(string owner, string vehicleId, bool want, out string reason)
        {
            reason = "Street Food is not running";
            var service = VendorRuntime.Current;
            return service == null ? VendorApiStatus.Off : service.RequestOwner(owner, vehicleId, want, out reason);
        }

        public static VendorApiStatus Renew(string owner, int nativeDay, int nativeMinute, out int leaseUntilDay, out int leaseUntilMinute)
        {
            leaseUntilDay = -1;
            leaseUntilMinute = -1;
            var service = VendorRuntime.Current;
            return service == null ? VendorApiStatus.Off : service.Renew(owner, nativeDay, nativeMinute, out leaseUntilDay, out leaseUntilMinute);
        }

        public static VendorApiStatus ReleaseAll(string owner)
        {
            var service = VendorRuntime.Current;
            return service == null ? VendorApiStatus.Off : service.ReleaseAll(owner);
        }

        public static VendorApiStatus Sell(string owner, string vehicleId, long openingId, string item,
            long priceCents, int nativeDay, int nativeMinute, out VendorApiSale? sale, out bool uncertain, out string reason)
        {
            sale = null;
            uncertain = false;
            reason = "Street Food is not running";
            var service = VendorRuntime.Current;
            return service == null ? VendorApiStatus.Off
                : service.Sell(owner, vehicleId, openingId, item, priceCents, nativeDay, nativeMinute, out sale, out uncertain, out reason);
        }
    }
}
