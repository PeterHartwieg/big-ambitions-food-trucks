using FoodTrucks;
using Entities;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Catering;
using Newtonsoft.Json;

static class Program
{
    static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    static CateringGig Gig(long id = 1) => new() { Id=id, PlaceId="ba:street_fourthavenue#34", Kind="party", PostedDay=1,
        ReplyByDay=2, GigDay=4, StartMinute=600, DurationMinutes=60, Guests=40, RequiredCategory="food", FeeCents=40000 };
    static TextMessage Confirmation(CateringOffer gig) => new() { messageKey="foodtrucks:message_bookings_booked",
        messageData = CateringDescription(gig) };
    static Dictionary<string,string> CateringDescription(CateringOffer g) => new() { ["day"]=g.GigDay.ToString(),
        ["start"]=g.StartMinute.ToString(), ["end"]=(g.StartMinute+g.DurationMinutes).ToString(), ["kind"]="party",
        ["guests"]="40", ["food"]="food", ["fee"]="$40000", ["place"]="34 #ba:street_fourthavenue", ["reply"]="2" };
    static void Main()
    {
        var state = new SaveState(); FoodTrucksSession.CurrentState=state; state.Catering.NextOfferId=20;
        Catering.Reset();
        for (var minute=0; minute<5; minute++) Catering.Run(state);
        Check(SaveGameManager.Current.Contacts.Count==0, "must not create contact");
        var c = new Contact { id="foodtrucks-bookings" }; SaveGameManager.Current.Contacts.Add(c);
        var gig=Gig(); state.Catering.Gigs.Add(gig);
        var legacy=Confirmation(gig); c.messagesQueue.Enqueue(legacy);
        Streets.AddressHelper.Street="THROW";
        Catering.Run(state); // Real migration caller, Ready=false, no building, native lookup throws.
        Check(legacy.messageData!["place"]=="34 4th Avenue", "history fallback must run before readiness");
        Check(legacy.messageData["guidance"]=="active" && legacy.messageData["metres"]=="60", "legacy active fields before readiness");
        Check(Streets.AddressHelper.LastKey=="ba:street_fourthavenue", "lookup must receive genuine legacy street key");
        var writes=FoodTrucksSession.Changes; var lookups=World.AddressCalls;
        Catering.Run(state); Check(writes==FoodTrucksSession.Changes && lookups==World.AddressCalls,"unchanged minute must not render history/write");
        World.Found=new() { building=new() { Address=new() { streetName= gig.PlaceId.Split('#')[0], streetNumber=34 } } };
        Streets.AddressHelper.Street="4th Avenue";
        World.Unavailable=true; Catering.RunStep(state,0);
        Check(state.Catering.Gigs.Count==0,"first-ready Step actually removed the same gig identity");
        World.Unavailable=false;
        Check(legacy.messageData["guidance"]=="inactive", "cancel flips guidance via caller");
        foreach (var transition in new[]{"complete","left","miss","midnight"})
        {
            var offer=Gig(2); offer.ReplyByDay=4; state.Catering.Offers.Add(offer); Catering.Run(state);
            var accepted=CateringOffers.Accept(new(),state.Catering,offer.Id,4,500,1);
            Check(accepted.Success,"Core accepts fresh offer after initial migration"); gig=accepted.Gig!;
            var msg=new TextMessage {messageKey="foodtrucks:message_bookings_booked",messageData=Catering.Describe(gig)};
            c.messagesQueue.Enqueue(msg); Catering.Run(state); // actual Describe + Core acceptance after initial migration
            Check(msg.messageData!["guidance"]=="active", "new acceptance active");
            if(transition=="miss" || transition=="midnight") CateringGigs.MissOverdue(new(),state.Catering,transition=="midnight"?5:4,700);
            else { gig.Status=GigStatus.Started; CateringGigs.Finish(new(),state.Catering,2,4,transition=="complete"?660:610); }
            Catering.Run(state); Check(msg.messageData["guidance"]=="inactive", transition+" flips in session");
            state.Catering.History.Clear();
        }
        // Malformed native payloads between valid records must not stop later repairs or loop migration.
        c.messagesQueue.Enqueue(null!); c.messagesQueue.Enqueue(new()); c.messagesQueue.Enqueue(new() { messageKey="foodtrucks:message_bookings_booked" });
        foreach(var field in new[]{"place","locationId"}) foreach(var value in new string?[]{null,"", "unparseable"})
        {
            var msg=Confirmation(Gig()); msg.messageData![field]=value!;
            if(field=="place") msg.messageData["locationId"]=Gig().PlaceId;
            c.messagesQueue.Enqueue(msg);
        }
        var missing=Confirmation(Gig()); missing.messageData!.Remove("place"); missing.messageData["locationId"]=Gig().PlaceId;
        c.messagesQueue.Enqueue(missing);
        var good=Confirmation(Gig()); c.messagesQueue.Enqueue(good);
        Catering.Reset(); Catering.Run(state);
        Check(good.messageData!["place"]=="34 4th Avenue", "good record after malformed ones repaired");
        foreach(var m in c.messagesQueue.Where(m=>m?.messageData!=null)) Check(!m.messageData!["place"].Contains("ba:street_"),"raw ID hidden");
        var saved=JsonConvert.SerializeObject(c.messagesQueue); c.messagesQueue=JsonConvert.DeserializeObject<Queue<TextMessage>>(saved)!;
        writes=FoodTrucksSession.Changes; Catering.Reset(); Catering.Run(state); Catering.Run(state);
        Check(writes==FoodTrucksSession.Changes,"save/reload idempotent");
        OfferLifecycle(c);
        StepLifecycle(c);
        ReadableReadiness(c);
        ExceptionsDoNotBlockSimulation(c);
        ReadOnlyDaily(c);
        writes=FoodTrucksSession.Changes;
        Commissary.Uninstalled=true; Catering.Reset(); Catering.Run(state); Check(writes==FoodTrucksSession.Changes,"uninstall guard");
        Console.WriteLine("PASS: bookings migration lifecycle");
    }
    static void OfferLifecycle(Contact c)
    {
        Commissary.Uninstalled=false; c.messagesQueue.Clear();
        var state=new SaveState(); FoodTrucksSession.CurrentState=state; state.Catering.NextOfferId=20;
        var offer=Gig(3); state.Catering.Offers.Add(offer);
        var open=Confirmation(offer); open.messageKey="foodtrucks:message_bookings_offer"; c.messagesQueue.Enqueue(open);
        SaveGameManager.Current.Day=2; Catering.Reset(); Catering.Run(state);
        Check(open.messageData!["guidance"]=="offer active","reply day keeps conditional hint");
        SaveGameManager.Current.Day=3; Catering.Run(state);
        Check(open.messageData["guidance"]=="offer active","still-open offer keeps hint even past deadline");
        state.Catering.Offers.Clear(); Catering.Run(state);
        Check(open.messageData["guidance"]=="offer dead","expired absent offer is dead");
        var accepted=Confirmation(offer); c.messagesQueue.Enqueue(accepted); Catering.Run(state);
        Check(open.messageData["guidance"]=="offer active","historic confirmation proves acceptance even if gig was removed");
        var none=new TextMessage {messageKey="foodtrucks:dialog_bookings_none",messageData=new(){["booked"]="2"}}; c.messagesQueue.Enqueue(none);
        var before=JsonConvert.SerializeObject(none); Catering.Run(state);
        Check(before==JsonConvert.SerializeObject(none),"dated nothing-new counter stays unchanged");
    }
    static void StepLifecycle(Contact c)
    {
        foreach(var transition in new[]{"cancel","complete","left","miss","midnight","arrival"})
        {
            var state=new SaveState(); FoodTrucksSession.CurrentState=state; state.Catering.NextOfferId=10;
            SaveGameManager.Current.Day=4;
            var gig=Gig(); state.Catering.Gigs.Add(gig);
            var msg=Confirmation(gig); c.messagesQueue.Clear(); c.messagesQueue.Enqueue(msg);
            var malformed=Confirmation(gig); malformed.messageData!["place"]=null!; malformed.messageData["locationId"]=null!;
            c.messagesQueue.Enqueue(malformed); c.messagesQueue.Enqueue(Confirmation(gig));
            state.Catering.Unpaid.Add(new(){ GigId=9,VehicleId="truck",PlaceId=gig.PlaceId,FeeCents=12345,Units=1 });
            World.Found=null; Catering.Reset(); var paid=Payments.Paid;
            Catering.RunStep(state,4900);
            Check(state.Catering.Unpaid.Count==0 && Payments.Paid==paid+12345,"bad history cannot block real PayUnpaid");
            Check(msg.messageData!["guidance"]=="active", "not ready must preserve booked gig");
            World.Found=new(){building=new(){Address=new(){streetName="ba:street_fourthavenue",streetNumber=34}}};
            SaveGameManager.Current.VehicleInstances.Clear();
            World.Unavailable=transition=="cancel";
            if(transition=="complete" || transition=="left") { gig.Status=GigStatus.Started; gig.VehicleId="truck"; }
            if(transition=="complete" || transition=="left" || transition=="arrival") SaveGameManager.Current.VehicleInstances.Add(new(){id="truck",Near=transition!="left"});
            long now=transition=="midnight"?5760:transition=="complete"?4980:transition=="miss"?5020:4910;
            if(transition=="midnight") SaveGameManager.Current.Day=5;
            Catering.RunStep(state,now);
            if(transition=="cancel") Check(state.Catering.Gigs.Count==0,"real cancellation removed gig without changing its identity");
            World.Unavailable=false;
            if(transition=="arrival") { Catering.RunStep(state,4912); Check(gig.Status==GigStatus.Started,"real Step arrival still runs"); Catering.RunStep(state,4980); }
            Check(msg.messageData["guidance"]=="inactive",transition+" must flip during actual Step");
            Check(Payments.Paid==paid+12345,"fee must not pay twice");
        }
    }

    static void ReadableReadiness(Contact c)
    {
        var state=new SaveState(); FoodTrucksSession.CurrentState=state; state.Catering.NextOfferId=10;
        var gig=Gig(); state.Catering.Gigs.Add(gig);
        SaveGameManager.Current.Day=2; SaveGameManager.Current.VehicleInstances.Clear();
        var msg=Confirmation(gig); msg.messageData!["place"]="34 4th Avenue"; msg.messageData["guidance"]="active";
        c.messagesQueue.Clear(); c.messagesQueue.Enqueue(msg);
        World.Found=null; Streets.AddressHelper.Street="THROW"; Catering.Reset();
        Catering.RunStep(state,1440);
        Check(msg.messageData["guidance"]=="active", "readable ID-less confirmation must never become inactive before readiness");
        World.Found=new(){building=new(){Address=new(){streetName="ba:street_fourthavenue",streetNumber=34}}};
        Streets.AddressHelper.Street="4th Avenue";
        Catering.RunStep(state,1440);
        Check(state.Catering.Gigs.Single()==gig && msg.messageData["guidance"]=="active", "readiness-only change reconciles active gig");
        var saved=JsonConvert.SerializeObject(c.messagesQueue); var writes=FoodTrucksSession.Changes;
        c.messagesQueue=JsonConvert.DeserializeObject<Queue<TextMessage>>(saved)!;
        Catering.Reset(); Catering.RunStep(state,1440); Catering.RunStep(state,1440);
        Check(saved==JsonConvert.SerializeObject(c.messagesQueue) && writes==FoodTrucksSession.Changes,"readable legacy queue reload is byte-stable without dirty mark");

        // Names can become available after buildings, without changing Ready or any booking/queue input.
        msg=c.messagesQueue.Single(); msg.messageData!["place"]="34 Fourth Avenue"; msg.messageData["guidance"]="keep wording";
        Streets.AddressHelper.Street="THROW"; Catering.Reset(); Catering.RunStep(state,1440);
        Check(msg.messageData["guidance"]=="keep wording" && state.Catering.Gigs.Count==1,"unknown native name preserves wording and gig");
        var scans=World.AddressCalls;
        Streets.AddressHelper.Street="Fourth Avenue"; Catering.Run(state);
        Check(msg.messageData["guidance"]=="active" && World.AddressCalls>scans,"native-name-only transition forces reconciliation");
        scans=World.AddressCalls; writes=FoodTrucksSession.Changes; Catering.Run(state);
        Check(scans==World.AddressCalls && writes==FoodTrucksSession.Changes,"one recheck after name readiness, then no scan/write");
        Streets.AddressHelper.Street="4th Avenue";
    }

    static void ExceptionsDoNotBlockSimulation(Contact c)
    {
        foreach(var simulationThrows in new[]{false,true})
        {
            var state=new SaveState(); state.Catering.NextOfferId=20; FoodTrucksSession.CurrentState=state;
            var gig=Gig(); gig.Status=GigStatus.Started; gig.VehicleId="truck"; state.Catering.Gigs.Add(gig);
            var badGig=Gig(2); badGig.Kind="throw"; badGig.GigDay=5; state.Catering.Gigs.Add(badGig);
            c.messagesQueue.Clear(); var first=Confirmation(gig); var last=Confirmation(gig);
            c.messagesQueue.Enqueue(first); c.messagesQueue.Enqueue(new(){messageKey="foodtrucks:message_bookings_booked",ThrowOnRead=true});
            c.messagesQueue.Enqueue(last);
            SaveGameManager.Current.Day=4; SaveGameManager.Current.VehicleInstances.Clear(); SaveGameManager.Current.VehicleInstances.Add(new(){id="truck"});
            state.Catering.Unpaid.Add(new(){GigId=9,VehicleId="truck",PlaceId=gig.PlaceId,FeeCents=12345,Units=1});
            Catering.Reset(); var paid=Payments.Paid; var errors=Log.AfterSimulationErrors;
            Catering.FailHistoryAfterSettle=true;
            var original=new Exception("injected settlement boundary failure"); Catering.SettlementFailure=simulationThrows?original:null;
            Exception? caught=null;
            try { Catering.RunStep(state,4980); } catch(Exception ex) { caught=ex; }
            Catering.FailHistoryAfterSettle=false; Catering.SettlementFailure=null;
            Check(simulationThrows?ReferenceEquals(caught,original):caught==null,"finally reconciliation must not mask simulation exception");
            Check(first.messageData!["place"]=="34 4th Avenue" && last.messageData!["place"]=="34 4th Avenue","bad record/description must not block other records");
            Check(state.Catering.History.Any(g=>g.Id==1) && state.Catering.Gigs.All(g=>g.Id!=1),"settlement still archives gig");
            Check(state.Catering.Unpaid.Count==0 && Payments.Paid==paid+12345,"history exceptions cannot block real PayUnpaid");
            Check(Log.AfterSimulationErrors==errors+1 && !FoodTrucksSession.ThrowNextMark,"finally MarkChange exception was injected and logged");
        }
    }

    static void ReadOnlyDaily(Contact c)
    {
        var state=new SaveState {WrittenByModVersion="99.0.0"}; FoodTrucksSession.CurrentState=state; FoodTrucksSession.CanWrite=false;
        c.messagesQueue.Clear(); c.messagesQueue.Enqueue(Confirmation(Gig()));
        var before=JsonConvert.SerializeObject(c.messagesQueue); var writes=FoodTrucksSession.Changes;
        Catering.Reset(); Catering.RunDay(); Catering.RunStep(state,1440); Catering.Run(state);
        EventLocations.RefreshBookingRadius(c);
        EventLocations.RefreshCateringHistory(c,Array.Empty<Dictionary<string,string>>());
        Check(before==JsonConvert.SerializeObject(c.messagesQueue) && writes==FoodTrucksSession.Changes,"newer writer daily/history/radius/Step must be read-only");
        FoodTrucksSession.CanWrite=true;
        Catering.Run(new SaveState());
        Check(before==JsonConvert.SerializeObject(c.messagesQueue) && writes==FoodTrucksSession.Changes,"detached state cannot rewrite current native contact");
    }

}
class Address { public string streetName = ""; public int streetNumber; }
class Building { public Address? Address; }
class CityBuildingController { public Building? building; }
namespace FoodTrucks
{
    static class World
    {
        internal static CityBuildingController? Found;
        internal static bool Unavailable; internal static int AddressCalls;
        internal static List<CityBuildingController> AllBuildings() => Found == null ? new() : new() { Found };
        internal static CityBuildingController? BuildingAt(string key) { AddressCalls++; return !Unavailable && key == "ba:street_fourthavenue#34" ? Found : null; }
    }
    static class FoodTrucksSession { internal static SaveState? CurrentState; internal static bool CanWrite=true, ThrowNextMark; internal static int Changes; internal static void MarkChanged() { Changes++; if(ThrowNextMark) { ThrowNextMark=false; throw new Exception("injected native dirty failure"); } } }
    static class Notices
    {
        internal static void SendText(Contact c,string key,Dictionary<string,string> d) { }
        internal static void Show(UI.Notification.NotificationType type, string key, Dictionary<string, string> data, float seconds) { }
    }
    static class CityPitches { internal static string Weekday(int d) => d.ToString(); internal static string Text(string key) => key switch {
        "foodtrucks:booking_inactive" => "inactive",
        "foodtrucks:booking_map_hint" => "active",
        "foodtrucks:catering_kind_party" => "party",
        "foodtrucks:category_food" => "food",
        "foodtrucks:offer_map_hint" => "offer active",
        "foodtrucks:offer_inactive" => "offer dead",
        "foodtrucks:catering_kind_throw" => throw new Exception("injected description failure"),
        _ => "Location unavailable" }; }
}
namespace Streets
{
    static class AddressHelper
    {
        internal static string Street = "4th Avenue";
        internal static IEnumerable<string> StreetDataNames => new[] { "ba:street_fourthavenue" };
        internal static string GetStreetNameLocalized(this Address a) => Street == "THROW" ? throw new Exception("missing native name") : Street;
        internal static string LastKey=""; internal static int Calls;
        internal static string GetStreetNameLocalized(this string key) { LastKey=key; Calls++; return Street == "THROW" ? throw new Exception("missing street data") : Street; }
        internal static string ToFormattedString(this Address a) => $"{a.streetNumber} {a.GetStreetNameLocalized()}";
    }
}
namespace UI.Guiders
{
    enum DirectionGuiderType { Destination }
    static class GuidersManager
    {
        internal static Address? Target;
        internal static void SetGuiderTarget(Address a, DirectionGuiderType type) => Target = a;
    }
}

namespace UI.Notification { enum NotificationType { Success } }

namespace Entities
{
    class Timestamp { public int Day; }
    class TextMessage { public string? messageKey; private Dictionary<string,string>? data; public Dictionary<string,string>? messageData { get => ThrowOnRead ? throw new Exception("injected record failure") : data; set => data=value; } [JsonIgnore] public bool ThrowOnRead; public Timestamp? timestamp; }
    class Contact { public string id=""; public Queue<TextMessage> messagesQueue = new(); }
}

namespace FoodTrucks {
static class Commissary { internal static bool Uninstalled; }
class Game { internal int Day=4; internal List<Contact> Contacts=new(); internal List<VehicleInstance> VehicleInstances=new(); internal string ActiveVehicleId=""; }
static class SaveGameManager { internal static Game Current=new(); }
static class CartSelling { internal static string Money(long cents) => "$"+cents; }
}

namespace FoodTrucks {
class VehicleInstance { internal string id="truck"; internal bool Near=true; }
class VehicleState { internal object? Transit; }
class Vector3 { internal static float Distance(Vector3 a,Vector3 b)=>0; }
class DemandTables { }
static class DemandData { internal static DemandTables Tables=new(); }
class TransactionInfo { internal TransactionInfo(string key,Dictionary<string,string> d) {} }
static class StreetSales { internal static bool CanRecordTotal(SaveState s,string v,int day,long fee,int units)=>true; internal static void RecordTotal(SaveState s,string v,int day,long fee,int units) {} }
static class Payments { internal static long Paid; internal static bool PayIn(long fee,TransactionInfo t) { Paid+=fee; return true; } }
static class Log { internal static int AfterSimulationErrors; internal static void Info(string s) {} internal static void Error(string s) { if(s.StartsWith("Catering history after simulation:")) AfterSimulationErrors++; } }
}
