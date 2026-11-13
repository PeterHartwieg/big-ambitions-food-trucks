using System;
using FoodTrucks.Core.Vendor;

namespace FoodTrucks.ModApi
{
    public enum VendorApiStatus { Ready = 0, Off = 1, NotYet = 2, Refused = 3, Failed = 4 }

    public sealed class VendorApiVehicle
    {
        public readonly string VehicleId;
        public readonly string Kind;
        public readonly bool Open;
        public readonly long OpeningId;
        public readonly string OpeningOwner;
        public readonly bool OpenedByCrew;
        public readonly bool EventOrCatering;
        public readonly int OpenedDay, OpenedMinute;
        public readonly int ClosesDay, ClosesMinute;
        public readonly float X, Y, Z;
        public readonly float QueueX, QueueY, QueueZ, QueueHeading;
        public readonly int MealsPerHour;
        public readonly bool MenuOverflow;
        public readonly string[] Items;
        public readonly long[] PriceCents;
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

    public sealed class VendorApiSale
    {
        public readonly int BookedDay;
        public readonly long Cents;
        public readonly int UnitsBefore, UnitsAfter;
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

        public static long Revision => VendorRuntime.Revision();

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
