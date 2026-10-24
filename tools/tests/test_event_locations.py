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
            (folder / 'Catering.cs').write_text('namespace FoodTrucks { static class Catering {' + source[start:end] + '} }')
            (folder / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup>
<Compile Include="{escape(str(ROOT / 'unity/FoodTrucks/Scripts/EventLocations.cs'))}" />
<Compile Include="{escape(str(ROOT / 'tools/tests/event_locations/Scenarios.cs'))}" />
</ItemGroup></Project>''')
            result = subprocess.run(['dotnet', 'run', '--project', str(folder / 'Harness.csproj'), '-v:q'],
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
        self.assertLess(pitches.index('DropUnknown(state, tables,'), pitches.index('CityEvents.Expire('))
