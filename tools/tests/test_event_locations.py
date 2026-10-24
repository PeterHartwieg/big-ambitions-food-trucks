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
                                    capture_output=True, text=True, timeout=120)
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
        migration = catering[catering.index('        private static void RefreshSavedMessages('):catering.index('        // Once a game minute')]
        self.assertNotIn('Bookings()', migration)
        self.assertNotIn('RefreshMessages(', step)
        self.assertNotIn('RefreshBookingRadius(', step)
        update = catering[catering.index('        private void Update()'):catering.index('        // One migration per load')]
        self.assertIn('Commissary.Uninstalled', update)
        self.assertIn('migrateRadius = migrateAddresses = true;', catering)
        self.assertEqual(catering.count('CateringGigs.CancelUnlocatable('), 1)
        self.assertLess(step.index('CateringGigs.CancelUnlocatable('), step.index('SettleEnded('))

    def test_catering_migration_does_not_restore_uninstalled_contact(self):
        with tempfile.TemporaryDirectory(prefix='ft-bookings-migration-') as directory:
            folder = Path(directory)
            source = (ROOT / 'unity/FoodTrucks/Scripts/Catering.cs').read_text()
            method = source[source.index('        private static void RefreshSavedMessages('):source.index('        // Once a game minute')]
            (folder / 'Catering.cs').write_text("""using System.Linq;
using FoodTrucks.Core.Catering;
using FoodTrucks.Core.Saves;
namespace FoodTrucks { static class Catering {
const string ContactId = "foodtrucks-bookings";
static bool migrateRadius, migrateAddresses;
internal static void Reset() => migrateRadius = migrateAddresses = true;
internal static void Run(SaveState state) => RefreshSavedMessages(state);
static System.Collections.Generic.Dictionary<string, string> Describe(CateringOffer offer) => new();
""" + method + '} }')
            (folder / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup>
<ProjectReference Include="{escape(str(ROOT / 'Core/FoodTrucks.Core.csproj'))}" />
<PackageReference Include="Newtonsoft.Json" Version="13.0.2" />
<Compile Include="{escape(str(ROOT / 'tools/tests/event_locations/MigrationScenarios.cs'))}" />
</ItemGroup></Project>''')
            result = subprocess.run(['dotnet', 'run', '--project', str(folder / 'Harness.csproj'), '-v:q'],
                                    capture_output=True, text=True, timeout=120)
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
                                    capture_output=True, text=True, timeout=120)
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
                                    capture_output=True, text=True, timeout=120)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn('PASS: controller cache', result.stdout)
