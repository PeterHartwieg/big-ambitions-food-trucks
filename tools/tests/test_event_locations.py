"""Run the production address adapter: street data IDs must never reach player text."""
from pathlib import Path
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]


class EventLocationTests(unittest.TestCase):
    def test_native_address_formatter_and_gps(self):
        with tempfile.TemporaryDirectory(prefix='ft-event-address-') as directory:
            folder = Path(directory)
            source = (ROOT / 'unity/FoodTrucks/Scripts/Catering.cs').read_text()
            start = source.index('        internal static string PlaceName(')
            end = source.index('\n        }', start) + len('\n        }')
            radius = next(line for line in source.splitlines() if 'public const float PlaceMetres =' in line)
            (folder / 'Catering.cs').write_text('namespace FoodTrucks { static class Catering {' + radius + source[start:end] + '} }')
            (folder / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup>
<PackageReference Include="Newtonsoft.Json" Version="13.0.2" />
<ProjectReference Include="{escape(str(ROOT / 'Core/FoodTrucks.Core.csproj'))}" />
<Compile Include="{escape(str(ROOT / 'unity/FoodTrucks/Scripts/EventLocations.cs'))}" />
<Compile Include="{escape(str(ROOT / 'tools/tests/event_locations/Scenarios.cs'))}" />
</ItemGroup></Project>''')
            result = subprocess.run(['dotnet', 'run', '--project', str(folder / 'Harness.csproj'), '-v:q', '--', str(ROOT)],
                                    capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn('PASS: native event addresses', result.stdout)

    def test_all_city_surfaces_use_resolved_address_and_keep_directions(self):
        pitches = (ROOT / 'unity/FoodTrucks/Scripts/CityPitches.cs').read_text()
        phone = (ROOT / 'unity/FoodTrucks/Scripts/CityLicensing.cs').read_text()
        self.assertNotIn('Text(place.Name)', pitches + phone)
        self.assertNotIn('Text(here.Value.Place.Name)', pitches)
        self.assertNotIn('city.PlaceId :', phone)
        self.assertIn('EventLocations.SetDestination(place.Address)', phone)
        self.assertIn('Upcoming(state).ToList()', phone)
        self.assertIn('EventLocations.ReadableAddress(p.Address) != null', pitches)
        self.assertLess(pitches.index('DropUnknown(state, tables);'), pitches.index('CityEvents.Expire('))
        catering = (ROOT / 'unity/FoodTrucks/Scripts/Catering.cs').read_text()
        step = catering[catering.index('        private void Step('):catering.index('        // The minute the truck is free')]
        self.assertLess(step.index('RefreshSavedMessages(state);'), step.index('SettleEnded('))
        migration = catering[catering.index('        internal static void RefreshSavedMessages('):catering.index('        // Once a game minute')]
        self.assertNotIn('Bookings()', migration)
        self.assertNotIn('RefreshMessages(', step)
        self.assertNotIn('RefreshBookingRadius(', step)
        update = catering[catering.index('        private void Update()'):catering.index('        // Address recovery is once per load')]
        self.assertIn('Commissary.Uninstalled', update)
        self.assertIn('migrateRadius = migrateAddresses = true;', catering)
        self.assertEqual(catering.count('CateringGigs.CancelUnlocatable('), 1)
        self.assertLess(step.index('CateringGigs.CancelUnlocatable('), step.index('SettleEnded('))

    def test_daily_offers_do_not_restore_uninstalled_contact(self):
        catering = (ROOT / 'unity/FoodTrucks/Scripts/Catering.cs').read_text()
        daily = catering[catering.index('        private static void OnNewDay()'):catering.index("        // Today's offers")]
        guard = 'if (Commissary.Uninstalled || !FoodTrucksSession.CanWrite || FoodTrucksSession.CurrentState == null) return;'
        self.assertEqual(daily[daily.index('{') + 1:].lstrip().splitlines()[0], guard)
        self.assertLess(daily.index(guard), daily.index('Bookings();'))
        self.assertLess(daily.index(guard), daily.index('Offer();'))

    def test_catering_migration_does_not_restore_uninstalled_contact(self):
        with tempfile.TemporaryDirectory(prefix='ft-bookings-migration-') as directory:
            folder = Path(directory)
            source = (ROOT / 'unity/FoodTrucks/Scripts/Catering.cs').read_text()
            method = source[source.index('        internal static void RefreshSavedMessages('):source.index('        // Once a game minute')]
            step = source[source.index('        private void Step('):source.index('        // The minute the truck is free')]
            settle = source[source.index('        private void SettleEnded('):source.index('        // Whether the truck could start')]
            describe = source[source.index('        internal static Dictionary<string, string> Describe('):source.index('        // "12 Fifth Street"')]
            daily = source[source.index('        private static void OnNewDay()'):source.index("        // Today's offers")]
            (folder / 'Catering.cs').write_text("""using System;
using System.Linq;
using System.Collections.Generic;
using Entities;
using FoodTrucks.Core.Catering;
using FoodTrucks.Core.Saves;
namespace FoodTrucks { class Catering {
const string ContactId = "foodtrucks-bookings";
public const float PlaceMetres = 60;
static bool migrateRadius, migrateAddresses;
static string? historyState;
internal static void Reset() { migrateRadius = migrateAddresses = true; historyState = null; }
internal static void Run(SaveState state) => RefreshSavedMessages(state);
internal static void RunDay() => OnNewDay();
static void Offer() { }
static string Text(string s) => CityPitches.Text(s);
static string PlaceName(string s) => EventLocations.Name(s);
static string Clock(int m) => m.ToString();
static string Category(string s) => s;
""" + method + describe + step + settle + daily + """
static Catering runner = new();
internal static bool FailHistoryAfterSettle;
internal static Exception? SettlementFailure;
internal static void RunStep(SaveState state, long now) => runner.Step(state, new(), now);
readonly Dictionary<(long Gig,string Vehicle),long> readySince=new();
readonly Dictionary<(string Place,string Vehicle),(long Since,Vector3 At)> nearSince=new();
readonly Dictionary<long,long> lastSeenNear=new();
const long StandMinutes=2, StandMinutesDriving=10;
static long GigStart(CateringOffer g) => (g.GigDay-1)*1440L+g.StartMinute;
static long BusyUntil(string id) => 0;
static Vector3 PositionOf(VehicleInstance v) => new();
static bool Near(VehicleInstance v,string p) => v.Near;
static bool Ready(SaveState s,CateringRules r,DemandTables t,CateringGig g,VehicleInstance v,VehicleState saved,long m) => true;
static List<(VehicleInstance Vehicle,VehicleState State,string Kind)> Caterers(SaveState s,CateringRules r) => SaveGameManager.Current.VehicleInstances.Select(v=>(v,new VehicleState(),"truck")).ToList();
static Contact Bookings() => SaveGameManager.Current.Contacts[0];
bool Arrive(SaveState s,CateringRules r,DemandTables t,CateringGig g,VehicleInstance v,VehicleState saved,string kind,long m) => CateringGigs.Arrive(r,s.Catering,g.Id,v.id,kind,(int)(m/1440)+1,(int)(m%1440),new[]{"hotdog"},new Dictionary<string,int>{{"hotdog",100}},_=>"food").Success;
void Settle(SaveState s,CateringRules r,DemandTables t,CateringGig g,VehicleInstance? truck,long m) {
 CateringGigs.Finish(r,s.Catering,g.Id,(int)(m/1440)+1,(int)(m%1440));
 if(FailHistoryAfterSettle) FoodTrucksSession.ThrowNextMark=true;
 if(SettlementFailure!=null) throw SettlementFailure;
}
} }""")
            (folder / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup>
<ProjectReference Include="{escape(str(ROOT / 'Core/FoodTrucks.Core.csproj'))}" />
<PackageReference Include="Newtonsoft.Json" Version="13.0.2" />
<Compile Include="{escape(str(ROOT / 'tools/tests/event_locations/MigrationScenarios.cs'))}" />
<Compile Include="{escape(str(ROOT / 'unity/FoodTrucks/Scripts/EventLocations.cs'))}" />
</ItemGroup></Project>''')
            result = subprocess.run(['dotnet', 'run', '--project', str(folder / 'Harness.csproj'), '-v:q'],
                                    capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn('PASS: bookings migration lifecycle', result.stdout)

    def test_city_day_cleanup_and_retry_when_addresses_load_late(self):
        with tempfile.TemporaryDirectory(prefix='ft-city-day-') as directory:
            folder = Path(directory)
            source = (ROOT / 'unity/FoodTrucks/Scripts/CityPitches.cs').read_text()
            pending = source[source.index('        private static SaveState? pendingAddresses;'):source.index('        // Once per city load:')]
            methods = source[source.index('        public static void NewDay('):source.index('        // Debug key for throwaway saves:')]
            (folder / 'CityPitches.cs').write_text('''using System;
using System.Linq;
using System.Collections.Generic;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Pitches;
namespace FoodTrucks { static class CityPitches {
private static PitchTables? Tables => DemandData.Pitches;
internal static string Text(string s) => s;
internal static string Clock(int m) => m.ToString();
internal static string Weekday(int d) => d.ToString();
''' + pending + methods + '} }')
            (folder / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup>
<PackageReference Include="Newtonsoft.Json" Version="13.0.2" />
<ProjectReference Include="{escape(str(ROOT / 'Core/FoodTrucks.Core.csproj'))}" />
<Compile Include="{escape(str(ROOT / 'tools/tests/event_locations/CityDayScenarios.cs'))}" />
</ItemGroup></Project>''')
            result = subprocess.run(['dotnet', 'run', '--project', str(folder / 'Harness.csproj'), '-v:q', '--', str(ROOT)],
                                    capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn('PASS: real city NewDay readiness', result.stdout)

    def test_building_lookup_cache_retries_and_resets(self):
        with tempfile.TemporaryDirectory(prefix='ft-world-cache-') as directory:
            folder = Path(directory)
            source = (ROOT / 'unity/FoodTrucks/Scripts/World.cs').read_text()
            methods = source[source.index('        private static List<(CityBuildingController Controller, Vector3[] Doors)>? buildings;'):source.index('        // The entrances of the building at an address;')]
            (folder / 'World.cs').write_text('using System; using System.Collections.Generic; using UnityEngine; namespace FoodTrucks { static partial class World {' + methods + '} }')
            (folder / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup>
<Compile Include="{escape(str(ROOT / 'tools/tests/event_locations/WorldCacheScenarios.cs'))}" />
</ItemGroup></Project>''')
            result = subprocess.run(['dotnet', 'run', '--project', str(folder / 'Harness.csproj'), '-v:q'],
                                    capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn('PASS: controller cache', result.stdout)
