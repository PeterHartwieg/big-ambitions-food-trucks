using System;
using System.Linq;
using FoodTrucks.Core.Catering;
using FoodTrucks.Core.Common;
using FoodTrucks.Core.Pitches;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Vendor;
using FoodTrucks.ModApi;
using Newtonsoft.Json.Linq;
using Xunit;
using static FoodTrucks.Core.Tests.Vendor.VendorTestHost;

namespace FoodTrucks.Core.Tests.Vendor;

public class VendorServiceTests
{
    private const string Cart = "cart-a";

    private static (VendorTestHost Host, VendorService Service) Leased(long now)
    {
        var host = new VendorTestHost { Now = now };
        host.AddVehicle(Cart);
        var service = new VendorService(host);
        Assert.Equal(VendorApiStatus.Ready, service.Renew(Owner, VendorBook.DayOf(now), VendorBook.MinuteOf(now), out _, out _));
        return (host, service);
    }

    private static (VendorTestHost Host, VendorService Service, long OpeningId) Granted(long requestAt, long openAt)
    {
        var (host, service) = Leased(requestAt);
        Assert.Equal(VendorApiStatus.Ready, service.RequestOwner(Owner, Cart, true, out _));
        host.Now = openAt;
        Assert.Equal(VendorApiStatus.Ready, service.Renew(Owner, VendorBook.DayOf(openAt), VendorBook.MinuteOf(openAt), out _, out _));
        Assert.Equal(Owner, host.Open(Cart, openAt));
        return (host, service, VendorBook.Entry(host.Save, Cart)!.OpeningId);
    }

    private static VendorApiVehicle ReadOne(VendorService service, string id = Cart)
    {
        service.Refresh();
        var into = new VendorApiVehicle[64];
        Assert.Equal(VendorApiStatus.Ready, service.Read(into, out var count));
        return into.Take(count).Single(v => v.VehicleId == id);
    }

    private static VendorApiStatus Sell(VendorService service, long openingId, long minute, out VendorApiSale? sale, out bool uncertain,
        out string reason, string item = "ba:itemname_hotdog", long cents = 450, string owner = Owner) =>
        service.Sell(owner, Cart, openingId, item, cents, VendorBook.DayOf(minute), VendorBook.MinuteOf(minute), out sale, out uncertain, out reason);

    [Fact]
    public void WithoutAConsumerAnOpeningWritesNothingAndKeepsItsOwnCustomers()
    {
        var host = new VendorTestHost { Now = At(3, 10, 0) };
        host.AddVehicle(Cart);
        var service = new VendorService(host);
        Assert.Equal(string.Empty, host.Open(Cart, At(3, 10, 0)));
        service.Tick();

        Assert.Null(host.Save.Vendor);
        Assert.True(VendorBook.OwnCustomers(host.Save, host.Vehicle(Cart), At(3, 10, 30)));
        Assert.False(service.Active);
        Assert.Equal(0, host.FactsCalls);
        Assert.Equal(0, host.Changes);
        Assert.Empty(host.Lines);
    }

    [Fact]
    public void ASaveWithoutAConsumerSavesExactlyAsBefore()
    {
        const string json = "{\"schemaVersion\":2,\"writtenByModVersion\":\"0.10.11\",\"saveCount\":4,\"licence\":{\"hasVendorLicence\":true,\"licenceExpiresOnDay\":9}," +
                            "\"vehicles\":[{\"vehicleId\":\"cart-a\",\"isOpen\":true,\"vehicleType\":\"cart\",\"hireStop\":0,\"service\":{\"randomState\":5,\"minute\":3,\"nextCustomerId\":1}}]," +
                            "\"crewTakings\":[{\"vehicleId\":\"cart-a\",\"day\":3,\"cents\":900,\"sales\":2}],\"someFutureField\":{\"x\":1}}";
        var codec = new SaveCodec();
        var loaded = codec.Load(json);
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        Assert.Null(loaded.State!.Vendor);
        Assert.DoesNotContain(loaded.Notes, n => n.Contains("Vendor"));

        var first = codec.Serialize(loaded.State);
        Assert.Null(JObject.Parse(first)["vendor"]);
        VendorBook.Opened(loaded.State, "cart-a", At(3, 11, 0), byCrew: true);
        Assert.Equal(first, codec.Serialize(loaded.State));
        Assert.Equal(first, codec.Serialize(codec.Load(first).State!));
    }

    [Fact]
    public void ReadingTheVehiclesNeverMakesAVendorBlock()
    {
        var host = new VendorTestHost { Now = At(3, 10, 0) };
        host.AddVehicle(Cart);
        var service = new VendorService(host);
        host.Open(Cart, At(3, 10, 0));
        var vehicle = ReadOne(service);

        Assert.Null(host.Save.Vendor);
        Assert.True(vehicle.Open);
        Assert.Equal(0, vehicle.OpeningId);
        Assert.Equal(string.Empty, vehicle.OpeningOwner);
        Assert.Equal(-1, vehicle.OpenedDay);
    }

    [Fact]
    public void AnOpeningInTheHourOfTheRequestStaysStreetFoodsAndTheNextLaterOneIsGranted()
    {
        var (host, service) = Leased(At(3, 10, 5));
        Assert.Equal(VendorApiStatus.Ready, service.RequestOwner(Owner, Cart, true, out _));

        host.Now = At(3, 10, 30);
        Assert.Equal(string.Empty, host.Open(Cart, At(3, 10, 30)));
        Assert.True(VendorBook.OwnCustomers(host.Save, host.Vehicle(Cart), At(3, 10, 31)));
        Assert.NotNull(VendorBook.Request(host.Save, Cart));
        Assert.Equal(string.Empty, ReadOne(service).OpeningOwner);
        host.Close(Cart);

        host.Now = At(3, 11, 0);
        Assert.Equal(Owner, host.Open(Cart, At(3, 11, 0)));
        var vehicle = ReadOne(service);
        Assert.Equal(Owner, vehicle.OpeningOwner);
        Assert.Equal(3, vehicle.OpenedDay);
        Assert.Equal(660, vehicle.OpenedMinute);
        Assert.False(VendorBook.OwnCustomers(host.Save, host.Vehicle(Cart), At(3, 11, 1)));
    }

    [Fact]
    public void ARepeatedRequestKeepsTheFirstHour()
    {
        var (host, service) = Leased(At(3, 10, 5));
        service.RequestOwner(Owner, Cart, true, out _);
        host.Now = At(3, 10, 50);
        Assert.Equal(VendorApiStatus.Ready, service.RequestOwner(Owner, Cart, true, out _));

        Assert.Equal(10 + 2 * 24, VendorBook.Request(host.Save, Cart)!.Hour);
        host.Now = At(3, 11, 0);
        Assert.Equal(Owner, host.Open(Cart, At(3, 11, 0)));
    }

    [Fact]
    public void AnOpenStreetFoodOpeningIsNeverTakenOverMidway()
    {
        var host = new VendorTestHost { Now = At(3, 9, 0) };
        host.AddVehicle(Cart);
        var service = new VendorService(host);
        host.Open(Cart, At(3, 9, 0));
        service.Renew(Owner, 3, 9 * 60, out _, out _);
        service.RequestOwner(Owner, Cart, true, out _);

        host.Now = At(3, 12, 0);
        service.Renew(Owner, 3, 12 * 60, out _, out _);
        Assert.Equal(string.Empty, ReadOne(service).OpeningOwner);
        Assert.True(VendorBook.OwnCustomers(host.Save, host.Vehicle(Cart), At(3, 12, 0)));
    }

    [Fact]
    public void PlayerOpenedEventAndCateringOpeningsStayWithStreetFood()
    {
        var (host, service) = Leased(At(3, 10, 5));
        service.RequestOwner(Owner, Cart, true, out _);
        host.Now = At(3, 11, 0);
        service.Renew(Owner, 3, 660, out _, out _);

        Assert.Equal(string.Empty, host.Open(Cart, At(3, 11, 0), byCrew: false));
        Assert.False(ReadOne(service).OpenedByCrew);
        host.Close(Cart);

        host.Save.Events.Events.Add(new CityEvent { Id = 1, Kind = "fair", PlaceId = "p", Day = 3, Start = 600, End = 900, Booked = 1, Places = 2,
            Holds = { new PlaceHold { VehicleId = Cart, From = At(3, 11, 0) } } });
        Assert.Equal(string.Empty, host.Open(Cart, At(3, 11, 1)));
        Assert.True(ReadOne(service).EventOrCatering);
        host.Close(Cart);
        host.Save.Events.Events.Clear();

        host.Save.Catering.Gigs.Add(new CateringGig { Id = 2, VehicleId = Cart, Status = GigStatus.Started });
        Assert.Equal(string.Empty, host.Open(Cart, At(3, 11, 2)));
        host.Close(Cart);
        host.Save.Catering.Gigs.Clear();

        Assert.Equal(Owner, host.Open(Cart, At(3, 11, 3)));
    }

    [Fact]
    public void AnEventTakingAnOwnedVehicleHandsItBack()
    {
        var (host, _, _) = Granted(At(3, 10, 5), At(3, 11, 0));
        host.Save.Events.Events.Add(new CityEvent { Id = 1, Kind = "fair", PlaceId = "p", Day = 3, Start = 600, End = 900, Booked = 1, Places = 1,
            Holds = { new PlaceHold { VehicleId = Cart, From = At(3, 11, 20) } } });

        Assert.True(VendorBook.OwnCustomers(host.Save, host.Vehicle(Cart), At(3, 11, 20)));
        Assert.Equal(string.Empty, VendorBook.Entry(host.Save, Cart)!.Owner);
    }

    [Fact]
    public void AVehicleRequestedByAnotherOwnerIsRefused()
    {
        var (host, service) = Leased(At(3, 10, 5));
        service.RequestOwner(Owner, Cart, true, out _);
        Assert.Equal(VendorApiStatus.Refused, service.RequestOwner("othermod", Cart, true, out var reason));
        Assert.Equal("requested by another owner", reason);
        Assert.Equal(VendorApiStatus.Refused, service.RequestOwner("Bad Owner", Cart, true, out _));
        Assert.Equal(VendorApiStatus.Refused, service.RequestOwner(Owner, "no-such-cart", true, out reason));
        Assert.Equal("no such food vehicle", reason);
    }

    [Fact]
    public void AnOpeningWithoutAHoldingLeaseStaysWithStreetFood()
    {
        var host = new VendorTestHost { Now = At(3, 10, 5) };
        host.AddVehicle(Cart);
        var service = new VendorService(host);
        service.RequestOwner(Owner, Cart, true, out _);

        host.Now = At(3, 11, 0);
        Assert.Equal(string.Empty, host.Open(Cart, At(3, 11, 0)));
        Assert.Contains(host.Lines, l => l.Contains("lease does not hold"));
    }

    [Fact]
    public void ALeaseThatRunsOutMidOpeningHandsBackWithAFreshQueue()
    {
        var (host, service, _) = Granted(At(3, 10, 5), At(3, 11, 0));
        var vehicle = host.Vehicle(Cart);
        vehicle.Service!.Queue.Add(new ServiceCustomer { Id = 1, Patience = 5 });
        vehicle.Service.NextCustomerId = 2;

        Assert.False(VendorBook.OwnCustomers(host.Save, vehicle, At(3, 11, 59)));
        Assert.True(VendorBook.OwnCustomers(host.Save, vehicle, At(3, 12, 0)));
        Assert.Empty(vehicle.Service!.Queue);
        Assert.Equal(SeededRandom.SeedFor(Cart, At(3, 12, 0)), vehicle.Service.RandomState);
        Assert.Equal(string.Empty, ReadOne(service).OpeningOwner);
    }

    [Fact]
    public void RenewExtendsTheLeaseToAnHourAfterTheGivenMinute()
    {
        var host = new VendorTestHost { Now = At(3, 23, 30) };
        host.AddVehicle(Cart);
        var service = new VendorService(host);

        Assert.Equal(VendorApiStatus.Ready, service.Renew(Owner, 3, 23 * 60 + 30, out var day, out var minute));
        Assert.Equal((4, 30), (day, minute));
        Assert.Equal(VendorApiStatus.Refused, service.Renew(Owner, 3, 23 * 60 + 31, out _, out _));
        Assert.Equal(VendorApiStatus.Refused, service.Renew(Owner, 3, 22 * 60 + 30, out _, out _));
        Assert.Equal(VendorApiStatus.Refused, service.Renew(Owner, 0, 10, out _, out _));
        Assert.Equal(VendorApiStatus.Refused, service.Renew(Owner, 3, 1440, out _, out _));
    }

    [Fact]
    public void TheTickHandsBackOnceTheLeaseIsOver()
    {
        var (host, service, _) = Granted(At(3, 10, 5), At(3, 11, 0));
        host.Now = At(3, 12, 0);
        service.Tick();

        Assert.Empty(host.Save.Vendor!.Leases);
        Assert.Equal(string.Empty, VendorBook.Entry(host.Save, Cart)!.Owner);
        Assert.Contains(host.Lines, l => l.Contains("handed back") && l.Contains("lease ran out"));
        Assert.NotNull(VendorBook.Request(host.Save, Cart));
    }

    [Fact]
    public void ReleasingTheVehicleResumesOwnCustomersAtOnce()
    {
        var (host, service, _) = Granted(At(3, 10, 5), At(3, 11, 0));
        host.Now = At(3, 11, 40);
        Assert.Equal(VendorApiStatus.Ready, service.RequestOwner(Owner, Cart, false, out _));

        Assert.True(VendorBook.OwnCustomers(host.Save, host.Vehicle(Cart), At(3, 11, 40)));
        Assert.Null(VendorBook.Request(host.Save, Cart));
        Assert.Equal(SeededRandom.SeedFor(Cart, At(3, 11, 40)), host.Vehicle(Cart).Service!.RandomState);
        host.Close(Cart);
        Assert.Equal(string.Empty, host.Open(Cart, At(3, 13, 0)));
    }

    [Fact]
    public void ReleaseAllDropsTheLeaseTheRequestsAndEveryOpening()
    {
        var (host, service, _) = Granted(At(3, 10, 5), At(3, 11, 0));
        Assert.Equal(VendorApiStatus.Ready, service.ReleaseAll(Owner));

        Assert.Empty(host.Save.Vendor!.Leases);
        Assert.Empty(host.Save.Vendor.Requests);
        Assert.True(VendorBook.OwnCustomers(host.Save, host.Vehicle(Cart), At(3, 11, 1)));
    }

    [Fact]
    public void ClosingEndsTheOwnership()
    {
        var (host, _, _) = Granted(At(3, 10, 5), At(3, 11, 0));
        host.Close(Cart);
        Assert.Equal(string.Empty, VendorBook.Entry(host.Save, Cart)!.Owner);
    }

    [Fact]
    public void ASaleBooksExactCentsOnTheDayAndReportsUnits()
    {
        var (host, service, opening) = Granted(At(3, 10, 5), At(3, 11, 0));
        host.Now = At(3, 11, 10);

        Assert.Equal(VendorApiStatus.Ready, Sell(service, opening, At(3, 11, 10), out var sale, out var uncertain, out _));
        Assert.False(uncertain);
        Assert.Equal(3, sale!.BookedDay);
        Assert.Equal(450, sale.Cents);
        Assert.Equal((20, 19), (sale.UnitsBefore, sale.UnitsAfter));
        Assert.Equal(1, sale.Sequence);
        Assert.Equal(VendorApiStatus.Ready, Sell(service, opening, At(3, 11, 10), out sale, out _, out _));
        Assert.Equal(2, sale!.Sequence);

        var taking = Assert.Single(host.Save.CrewTakings);
        Assert.Equal((Cart, 3, 900L, 2), (taking.VehicleId, taking.Day, taking.Cents, taking.Sales));
        Assert.Equal(2, host.Vehicle(Cart).LifetimeSales);
        Assert.Equal(900, Assert.Single(host.Save.DailySales).RevenueCents);
        Assert.Equal(19 - 1, ReadOne(service).Units[0]);
    }

    [Fact]
    public void SalesAroundMidnightBookOnTheirOwnDayAndAPastDayIsRefused()
    {
        var (host, service, opening) = Granted(At(3, 22, 5), At(3, 23, 0));
        host.Now = At(3, 23, 59);
        service.Renew(Owner, 3, 1439, out _, out _);
        Assert.Equal(VendorApiStatus.Ready, Sell(service, opening, At(3, 23, 59), out var late, out _, out _));
        Assert.Equal(3, late!.BookedDay);

        host.Now = At(4, 0, 0);
        Assert.Equal(VendorApiStatus.Refused, Sell(service, opening, At(3, 23, 59), out _, out _, out var reason));
        Assert.Equal("not the current minute", reason);
        Assert.Equal(VendorApiStatus.Ready, Sell(service, opening, At(4, 0, 0), out var early, out _, out _));
        Assert.Equal(4, early!.BookedDay);

        Assert.Equal(new[] { (3, 450L), (4, 450L) }, host.Save.CrewTakings.OrderBy(t => t.Day).Select(t => (t.Day, t.Cents)));
    }

    [Fact]
    public void EveryRefusalComesBeforeAnyEffect()
    {
        var (host, service, opening) = Granted(At(3, 10, 5), At(3, 11, 0));
        host.Now = At(3, 11, 10);
        var now = At(3, 11, 10);

        void Refused(string why, VendorApiStatus status)
        {
            Assert.Equal(VendorApiStatus.Refused, status);
            Assert.Empty(host.Save.CrewTakings);
            Assert.Empty(host.Save.DailySales);
            Assert.Empty(host.Sales);
            Assert.Equal(0, VendorBook.Entry(host.Save, Cart)!.Sequence);
        }

        Refused("opening", Sell(service, opening + 1, now, out _, out _, out var reason));
        Assert.Equal("opening changed", reason);
        Refused("owner", Sell(service, opening, now, out _, out _, out reason, owner: "othermod"));
        Assert.Equal("not the owner of this opening", reason);
        Refused("price", Sell(service, opening, now, out _, out _, out reason, cents: 449));
        Assert.Equal("price changed", reason);
        Refused("menu", Sell(service, opening, now, out _, out _, out reason, item: "ba:itemname_burger"));
        Assert.Equal("item not on the menu", reason);
        Refused("future", Sell(service, opening, now + 1, out _, out _, out reason));
        Assert.Equal("minute not reached yet", reason);
        Refused("earlier", Sell(service, opening, now - 1, out _, out _, out reason));
        Assert.Equal("not the current minute", reason);

        host.Vehicles[Cart].MealsPerHour = 0;
        Refused("serving", Sell(service, opening, now, out _, out _, out reason));
        Assert.Equal("nobody serving", reason);
        host.Vehicles[Cart].MealsPerHour = 30;

        host.Vehicles[Cart].Units = new[] { 0 };
        Refused("stock", Sell(service, opening, now, out _, out _, out reason));
        Assert.Equal("sold out", reason);
        host.Vehicles[Cart].Units = new[] { 20 };

        host.Vehicle(Cart).LifetimeSales = int.MaxValue;
        Refused("ledger", Sell(service, opening, now, out _, out _, out reason));
        Assert.Equal("the street food record is full", reason);
        host.Vehicle(Cart).LifetimeSales = 0;

        host.Vehicle(Cart).Shifts.Add(new ShiftPlan { Day = 3, Legs = { new ShiftLeg { Kind = FoodTrucks.Core.Fleet.LegKind.Open, StopIndex = 0, Start = At(3, 11, 0), End = At(3, 11, 10) } } });
        Refused("close", Sell(service, opening, now, out _, out _, out reason));
        Assert.Equal("minute after the planned close", reason);
        host.Vehicle(Cart).Shifts.Clear();

        host.Now = At(3, 12, 0);
        Refused("lease", Sell(service, opening, At(3, 12, 0), out _, out _, out reason));
        Assert.Equal("not the owner of this opening", reason);

        host.Close(Cart);
        Refused("closed", Sell(service, opening, At(3, 12, 0), out _, out _, out reason));
        Assert.Equal("vehicle closed", reason);
    }

    [Fact]
    public void AFailureAfterTheLedgerRecordIsUncertainAndBeforeItIsNot()
    {
        var (host, service, opening) = Granted(At(3, 10, 5), At(3, 11, 0));
        host.Now = At(3, 11, 10);

        host.ThrowBeforeRecord = true;
        Assert.Equal(VendorApiStatus.Failed, Sell(service, opening, At(3, 11, 10), out var sale, out var uncertain, out _));
        Assert.False(uncertain);
        Assert.Null(sale);
        Assert.Empty(host.Save.CrewTakings);

        host.ThrowBeforeRecord = false;
        host.ThrowAfterRecord = true;
        Assert.Equal(VendorApiStatus.Failed, Sell(service, opening, At(3, 11, 10), out sale, out uncertain, out var reason));
        Assert.True(uncertain);
        Assert.Null(sale);
        Assert.Contains("do not retry", reason);
        Assert.Equal(450, Assert.Single(host.Save.CrewTakings).Cents);
        Assert.Equal(1, VendorBook.Entry(host.Save, Cart)!.Sequence);
    }

    [Fact]
    public void AnOpeningStreetFoodHoldsCannotBeSoldThrough()
    {
        var (host, service) = Leased(At(3, 10, 5));
        host.Open(Cart, At(3, 10, 30));
        var opening = VendorBook.Entry(host.Save, Cart)!.OpeningId;
        host.Now = At(3, 10, 40);

        Assert.Equal(VendorApiStatus.Refused, Sell(service, opening, At(3, 10, 40), out _, out _, out var reason));
        Assert.Equal("not the owner of this opening", reason);
    }

    [Fact]
    public void SixtyFourVehiclesAreReadAndSixtyFiveAreRefused()
    {
        var host = new VendorTestHost { Now = At(3, 10, 0) };
        for (var i = 0; i < 64; i++) host.AddVehicle($"v{i:D2}");
        var service = new VendorService(host);
        var into = new VendorApiVehicle[64];

        Assert.Equal(VendorApiStatus.Ready, service.Read(into, out var count));
        Assert.Equal(64, count);
        Assert.Equal(VendorApiStatus.Refused, service.Read(new VendorApiVehicle[63], out count));
        Assert.Equal(0, count);

        host.AddVehicle("v64");
        var before = service.Revision;
        service.Refresh();
        Assert.NotEqual(before, service.Revision);
        Assert.Equal(VendorApiStatus.Refused, service.Read(new VendorApiVehicle[100], out count));
        Assert.Equal(0, count);
    }

    [Fact]
    public void EightMenuItemsAreListedAndNineOverflow()
    {
        var host = new VendorTestHost { Now = At(3, 10, 0) };
        var eight = Enumerable.Range(0, 8).Select(i => $"item{7 - i}").ToArray();
        host.AddVehicle("eight", items: eight);
        host.AddVehicle("nine", items: Enumerable.Range(0, 9).Select(i => $"item{i}").ToArray());
        var service = new VendorService(host);

        var listed = ReadOne(service, "eight");
        Assert.False(listed.MenuOverflow);
        Assert.Equal(eight.OrderBy(i => i, StringComparer.Ordinal), listed.Items);
        Assert.Equal(8, listed.PriceCents.Length);
        var overflow = ReadOne(service, "nine");
        Assert.True(overflow.MenuOverflow);
        Assert.Empty(overflow.Items);
        Assert.Empty(overflow.PriceCents);
        Assert.Empty(overflow.Units);
    }

    [Fact]
    public void AnUnchangedRevisionHandsOutTheSameCopies()
    {
        var host = new VendorTestHost { Now = At(3, 10, 0) };
        host.AddVehicle(Cart);
        var service = new VendorService(host);
        var first = ReadOne(service);
        var revision = service.Revision;

        service.Refresh();
        Assert.Equal(revision, service.Revision);
        Assert.Same(first, ReadOne(service));

        host.Vehicles[Cart].Units = new[] { 3 };
        service.Refresh();
        Assert.NotEqual(revision, service.Revision);
        Assert.Equal(3, ReadOne(service).Units[0]);
    }

    [Fact]
    public void ANewServiceStartsAtANewRevision()
    {
        var host = new VendorTestHost();
        var a = new VendorService(host).Revision;
        var b = new VendorService(host).Revision;
        Assert.True(b > a);
    }

    [Fact]
    public void ReadOnlyOrMissingDataIsOffOrNotYet()
    {
        var host = new VendorTestHost { Status = VendorApiStatus.Off };
        host.AddVehicle(Cart);
        var service = new VendorService(host);
        Assert.Equal(VendorApiStatus.Off, service.Read(new VendorApiVehicle[64], out var count));
        Assert.Equal(0, count);
        Assert.Equal(VendorApiStatus.Off, service.RequestOwner(Owner, Cart, true, out _));
        host.Status = VendorApiStatus.NotYet;
        Assert.Equal(VendorApiStatus.NotYet, service.Renew(Owner, 1, 0, out _, out _));
        Assert.Null(host.Save.Vendor);
    }

    [Fact]
    public void TheGrantAndLeaseSurviveASaveAndLoad()
    {
        var (host, _, opening) = Granted(At(3, 10, 5), At(3, 11, 0));
        var codec = new SaveCodec();
        var json = codec.Serialize(host.Save);
        var loaded = codec.Load(json);

        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        var entry = VendorBook.Entry(loaded.State!, Cart)!;
        Assert.Equal((opening, Owner), (entry.OpeningId, entry.Owner));
        Assert.Equal(At(3, 12, 0), VendorBook.Lease(loaded.State!, Owner)!.Until);
        Assert.Equal(json, codec.Serialize(loaded.State!));

        var vehicle = StreetSales.Find(loaded.State!, Cart)!;
        Assert.False(VendorBook.OwnCustomers(loaded.State!, vehicle, At(3, 11, 30)));
        Assert.True(VendorBook.OwnCustomers(loaded.State!, vehicle, At(3, 12, 0)));
    }

    [Fact]
    public void OpeningIdsKeepRisingAcrossAReload()
    {
        var (host, _, opening) = Granted(At(3, 10, 5), At(3, 11, 0));
        host.Close(Cart);
        var codec = new SaveCodec();
        host.Save = codec.Load(codec.Serialize(host.Save)).State!;
        host.Open(Cart, At(3, 11, 0));
        Assert.True(VendorBook.Entry(host.Save, Cart)!.OpeningId > opening);
    }

    [Fact]
    public void AnOwnerLeftOnAClosedVehicleIsClearedOnLoad()
    {
        var (host, _, _) = Granted(At(3, 10, 5), At(3, 11, 0));
        host.Vehicle(Cart).IsOpen = false;
        var loaded = new SaveCodec().Load(new SaveCodec().Serialize(host.Save));
        Assert.Equal(string.Empty, VendorBook.Entry(loaded.State!, Cart)!.Owner);
    }

    [Fact]
    public void ANewerVendorBlockIsKeptUnchangedAndTheApiRefused()
    {
        const string block = "{\"version\":2,\"leases\":[{\"owner\":\"persistentcity\",\"until\":5000,\"extra\":true}],\"somethingNew\":[1,2]}";
        var json = "{\"schemaVersion\":2,\"vehicles\":[{\"vehicleId\":\"cart-a\"}],\"vendor\":" + block + "}";
        var codec = new SaveCodec();
        var loaded = codec.Load(json);

        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        Assert.Null(loaded.State!.Vendor);
        Assert.Contains(loaded.Notes, n => n.Contains("newer Street Food"));
        Assert.True(JToken.DeepEquals(JToken.Parse(block), JObject.Parse(codec.Serialize(loaded.State))["vendor"]));

        var host = new VendorTestHost { Save = loaded.State, Now = At(3, 10, 0) };
        host.Vehicles[Cart] = new VendorFacts { VehicleId = Cart };
        var service = new VendorService(host);
        Assert.Equal(VendorApiStatus.Refused, service.RequestOwner(Owner, Cart, true, out var reason));
        Assert.Contains("newer", reason);
        Assert.Equal(VendorApiStatus.Refused, service.Renew(Owner, 3, 600, out _, out _));
        Assert.Null(loaded.State.Vendor);
    }

    [Fact]
    public void AnUnreadableVendorBlockIsDroppedAndTheSaveStillLoads()
    {
        var json = "{\"schemaVersion\":2,\"vehicles\":[{\"vehicleId\":\"cart-a\"}],\"vendor\":{\"version\":1,\"leases\":[{\"owner\":\"Bad Owner\",\"until\":1}]}}";
        var loaded = new SaveCodec().Load(json);

        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        Assert.Null(loaded.State!.Vendor);
        Assert.Contains(loaded.Notes, n => n.Contains("unreadable"));
    }

    [Fact]
    public void AnEarlierMinuteOfTheSameDayIsRefused()
    {
        var (host, service, opening) = Granted(At(3, 10, 5), At(3, 11, 0));
        host.Now = At(3, 11, 30);

        Assert.Equal(VendorApiStatus.Refused, Sell(service, opening, At(3, 11, 20), out var sale, out _, out var reason));
        Assert.Equal("not the current minute", reason);
        Assert.Null(sale);
        Assert.Empty(host.Save.CrewTakings);
        Assert.Empty(host.Sales);
    }

    [Fact]
    public void ABackdatedSaleAfterTheLeaseRanOutIsRefusedBeforeTheTickExpiresIt()
    {
        var (host, service, opening) = Granted(At(3, 10, 5), At(3, 11, 0));
        host.Now = At(3, 12, 1);

        Assert.Equal(VendorApiStatus.Refused, Sell(service, opening, At(3, 11, 59), out _, out _, out var reason));
        Assert.Equal("not the current minute", reason);
        Assert.Equal(VendorApiStatus.Refused, Sell(service, opening, At(3, 12, 1), out _, out _, out reason));
        Assert.Equal("not the owner of this opening", reason);
        Assert.Empty(host.Save.CrewTakings);
        Assert.Empty(host.Sales);

        var vehicle = host.Vehicle(Cart);
        Assert.Equal(At(3, 12, 0), VendorBook.Entry(host.Save, Cart)!.HandedBackAt);
        Assert.Equal(SeededRandom.SeedFor(Cart, At(3, 12, 0)), vehicle.Service!.RandomState);
        Assert.False(VendorBook.OwnCustomers(host.Save, vehicle, At(3, 11, 59)));
        Assert.True(VendorBook.OwnCustomers(host.Save, vehicle, At(3, 12, 0)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AReleaseNoticedAfterTheLeaseRanOutHandsBackAtTheLeasesEnd(bool all)
    {
        var (host, service, _) = Granted(At(3, 10, 5), At(3, 11, 0));
        var vehicle = host.Vehicle(Cart);
        host.Now = At(3, 12, 20);
        Assert.Equal(VendorApiStatus.Ready, all ? service.ReleaseAll(Owner) : service.RequestOwner(Owner, Cart, false, out _));

        Assert.Equal(At(3, 12, 0), VendorBook.Entry(host.Save, Cart)!.HandedBackAt);
        Assert.False(VendorBook.OwnCustomers(host.Save, vehicle, At(3, 11, 55)));
        for (var minute = At(3, 12, 0); minute < At(3, 12, 20); minute++)
            Assert.True(VendorBook.OwnCustomers(host.Save, vehicle, minute));
    }

    [Fact]
    public void ACatchUpBehindAnExplicitReleaseGetsNoStreetFoodCustomersBeforeIt()
    {
        var (host, service, _) = Granted(At(3, 10, 5), At(3, 11, 0));
        var vehicle = host.Vehicle(Cart);
        host.Now = At(3, 11, 40);
        Assert.Equal(VendorApiStatus.Ready, service.RequestOwner(Owner, Cart, false, out _));

        for (var minute = At(3, 11, 35); minute < At(3, 11, 40); minute++)
            Assert.False(VendorBook.OwnCustomers(host.Save, vehicle, minute));
        Assert.True(VendorBook.OwnCustomers(host.Save, vehicle, At(3, 11, 40)));
        Assert.Equal(At(3, 11, 40), VendorBook.Entry(host.Save, Cart)!.HandedBackAt);
    }

    [Fact]
    public void ACatchUpBehindALeaseExpiryGetsNoStreetFoodCustomersBeforeIt()
    {
        var (host, service, _) = Granted(At(3, 10, 5), At(3, 11, 0));
        var vehicle = host.Vehicle(Cart);
        host.Now = At(3, 12, 10);
        service.Tick();

        Assert.False(VendorBook.OwnCustomers(host.Save, vehicle, At(3, 11, 55)));
        Assert.False(VendorBook.OwnCustomers(host.Save, vehicle, At(3, 11, 59)));
        Assert.True(VendorBook.OwnCustomers(host.Save, vehicle, At(3, 12, 0)));

        var loaded = new SaveCodec().Load(new SaveCodec().Serialize(host.Save)).State!;
        Assert.False(VendorBook.OwnCustomers(loaded, StreetSales.Find(loaded, Cart)!, At(3, 11, 59)));
        host.Close(Cart);
        host.Open(Cart, At(3, 12, 30), byCrew: false);
        Assert.Null(VendorBook.Entry(host.Save, Cart)!.HandedBackAt);
        Assert.True(VendorBook.OwnCustomers(host.Save, vehicle, At(3, 12, 30)));
    }

    [Fact]
    public void AtTheRequestCapVanishedVehiclesMakeRoom()
    {
        var host = new VendorTestHost { Now = At(3, 10, 0) };
        for (var i = 0; i < VendorBook.MaxVehicles; i++) host.AddVehicle($"v{i:D2}");
        host.AddVehicle("new");
        var service = new VendorService(host);
        for (var i = 0; i < VendorBook.MaxVehicles; i++)
            Assert.Equal(VendorApiStatus.Ready, service.RequestOwner(Owner, $"v{i:D2}", true, out _));
        Assert.Equal(VendorApiStatus.Refused, service.RequestOwner(Owner, "new", true, out var reason));
        Assert.Equal("too many requests", reason);

        host.Vehicles.Remove("v07");
        Assert.Equal(VendorApiStatus.Ready, service.RequestOwner(Owner, "new", true, out _));
        Assert.Null(VendorBook.Request(host.Save, "v07"));
        Assert.Equal(VendorBook.MaxVehicles, host.Save.Vendor!.Requests.Count);
    }

    [Fact]
    public void ASaleRebuildsOnlyTheSoldVehiclesCopy()
    {
        var (host, service, opening) = Granted(At(3, 10, 5), At(3, 11, 0));
        host.AddVehicle("cart-b");
        host.Now = At(3, 11, 10);
        var other = ReadOne(service, "cart-b");
        var revision = service.Revision;
        var calls = host.FactsCalls;

        Assert.Equal(VendorApiStatus.Ready, Sell(service, opening, At(3, 11, 10), out _, out _, out _));
        Assert.Equal(calls, host.FactsCalls);
        Assert.NotEqual(revision, service.Revision);
        var into = new VendorApiVehicle[64];
        Assert.Equal(VendorApiStatus.Ready, service.Read(into, out var count));
        Assert.Equal(19, into.Take(count).Single(v => v.VehicleId == Cart).Units[0]);
        Assert.Same(other, into.Take(count).Single(v => v.VehicleId == "cart-b"));
    }

    [Fact]
    public void ATrucksWindowIsOnItsRightAndItsLineRunsTowardsTheBack()
    {
        var (x, z, qx, qz, heading) = VendorGeometry.Window("truck", 1, 10, 20, 0);
        Assert.Equal(12, x, 6);
        Assert.Equal(20 - 1.10, z, 6);
        Assert.Equal(12, qx, 6);
        Assert.Equal(20 - 2.10, qz, 6);
        Assert.Equal(180, heading, 6);

        var cart = VendorGeometry.Window("cart", -1, 0, 0, 0);
        Assert.Equal(-1.05, cart.X, 6);
        Assert.Equal(0, cart.Z, 6);
        Assert.Equal(-1.85, cart.QueueX, 6);
        Assert.Equal(270, cart.QueueHeading, 6);
    }

    [Fact]
    public void MealsPerHourFollowsOrderTimeSpeedAndServers()
    {
        Assert.Equal(30, VendorGeometry.MealsPerHour(2, 1, 1));
        Assert.Equal(45, VendorGeometry.MealsPerHour(2, 0.75, 2));
        Assert.Equal(0, VendorGeometry.MealsPerHour(2, 1, 0));
        Assert.Equal(0, VendorGeometry.MealsPerHour(0, 1, 1));
    }
}
