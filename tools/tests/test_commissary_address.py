"""Commissary address and map/GPS access: the production contact-header postfix, location accessor and BizMan rented-space
panel against controlled stand-ins (tools/tests/commissary_address/Scenarios.cs), plus the patch registration, the
game members it binds and the new locale keys."""
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / 'unity/FoodTrucks/Scripts'


def method(filename, signature):
    source = (SCRIPTS / filename).read_text(encoding='utf-8')
    start = source.index(signature)
    end = source.index('{', start) + 1
    depth = 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


def header_postfixes():
    """The handlers Patches.cs registers as postfixes on ContactsApp.SetUpHeader(Contact)."""
    flat = ' '.join((SCRIPTS / 'Patches.cs').read_text(encoding='utf-8').split())
    return re.findall(r'Patch\(typeof\(UI\.Smartphone\.Apps\.Contacts\.ContactsApp\), "SetUpHeader", new\[\] \{ typeof\(Contact\) \}, '
                      r'typeof\(ModContacts\), postfix: nameof\(ModContacts\.(\w+)\)\);', flat)


def extracted():
    """ModContacts' static fields and registered header postfixes; BizManVehiclePage.CommissarySpace."""
    contacts = (SCRIPTS / 'ModContacts.cs').read_text(encoding='utf-8')
    fields = '\n'.join(line for line in contacts.splitlines() if re.match(r'\s{8}private static [^(=]*;\s*$', line))
    handlers = '\n'.join(method('ModContacts.cs', f'internal static void {name}(') for name in header_postfixes())
    panel = method('BizManVehiclePage.cs', 'private static void CommissarySpace(').replace('private static void', 'internal static void', 1)
    names = ', '.join(json.dumps(n) for n in header_postfixes())
    return f'''using System; using System.Linq; using Entities; using FoodTrucks.Core.Saves; using UnityEngine;
namespace FoodTrucks {{
static class Hooks {{ public static readonly string[] HeaderPostfixes = {{ {names} }}; }}
static partial class ModContacts {{
{fields}
{handlers}
}}
static partial class BizManVehiclePage {{
{panel}
}}
}}
'''


def run_harness(root=ROOT):
    with tempfile.TemporaryDirectory(prefix='ft-commissary-address-') as directory:
        folder = Path(directory)
        (folder / 'Extracted.cs').write_text(extracted(), encoding='utf-8')
        location = SCRIPTS / 'CommissaryLocation.cs'
        include = f'<Compile Include="{escape(str(location))}" />' if location.is_file() else ''
        (folder / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup>
<PackageReference Include="Newtonsoft.Json" Version="13.0.2" />
<ProjectReference Include="{escape(str(ROOT / 'Core/FoodTrucks.Core.csproj'))}" />
<Compile Include="{escape(str(SCRIPTS / 'EventLocations.cs'))}" />
{include}
<Compile Include="{escape(str(ROOT / 'tools/tests/commissary_address/Scenarios.cs'))}" />
</ItemGroup></Project>''', encoding='utf-8')
        result = subprocess.run(['dotnet', 'run', '--project', str(folder / 'Harness.csproj'), '-v:q', '--', str(root)],
                                capture_output=True, text=True)
        return result


# ContactsApp as ILSpy shows it in BigAmbitions.dll (SHA-256 9427561B...E911, checked 28 October 2026): the patched
# method, and the private fields the postfix binds by name.
NATIVE_TYPE = 'UI.Smartphone.Apps.Contacts.ContactsApp'
NATIVE_METHOD = 'private void SetUpHeader(Contact contact)'
NATIVE_FIELDS = ('private Button viewOnMapButton;', 'private GameObject addressGameObject;',
                 'private TextLocalizationComponent addressLabel;')
HANDLER_PARAMETERS = ['Contact contact', 'UnityEngine.UI.Button ___viewOnMapButton', 'UnityEngine.GameObject ___addressGameObject',
                      'Localizor.LanguageChangeEvent.TextLocalizationComponent ___addressLabel']
# The game's map focus the View on map button reproduces (ContactsApp.ViewOnMapButtonOnClick) and the CityMap members used.
NATIVE_MAP = ('public CityBuildingController buildingToFocus;', 'public static bool IsOpen', 'public void Toggle()',
              'public void FocusOnBuilding(CityBuildingController buildingController)')

NEW_KEYS = {
    'foodtrucks:commissary_address_unavailable': None,
    'foodtrucks:bizman_page_space_address': None,
    'foodtrucks:bizman_page_space_destination': None,
    'foodtrucks:bizman_page_space_bay_hint': None,
    'foodtrucks:message_commissary_welcome_phone_address': 'foodtrucks:message_commissary_welcome_phone',
    'foodtrucks:dialog_commissary_start_spaces_address': 'foodtrucks:dialog_commissary_start_spaces',
    'foodtrucks:dialog_commissary_start_standing_address': 'foodtrucks:dialog_commissary_start_standing',
    'foodtrucks:message_commissary_space_rented_address': 'foodtrucks:message_commissary_space_rented',
    'foodtrucks:message_commissary_space_moved_address': 'foodtrucks:message_commissary_space_moved',
}


def placeholders(text):
    return set(re.findall(r'\{(\w+)\}', text))


class CommissaryAddressTests(unittest.TestCase):
    def test_header_bizman_and_messages_against_the_production_code(self):
        result = run_harness()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('PASS: commissary address header, BizMan and messages', result.stdout)

    def test_header_postfix_registered_with_the_standard_helper(self):
        self.assertEqual(header_postfixes(), ['HeaderPostfix'])
        patches = (SCRIPTS / 'Patches.cs').read_text(encoding='utf-8')
        # The standard helper logs Active / Not active per patch.
        self.assertIn('Log.Error($"Patch {name}: Not active, method not found.");', patches)

    def test_handler_parameters_are_the_game_names_harmony_binds(self):
        source = (SCRIPTS / 'ModContacts.cs').read_text(encoding='utf-8')
        start = source.index('(', source.index(' HeaderPostfix(')) + 1
        self.assertEqual([' '.join(p.split()) for p in source[start:source.index(')', start)].split(',')], HANDLER_PARAMETERS)
        self.assertIn('Contact contact', NATIVE_METHOD)
        for parameter in HANDLER_PARAMETERS[1:]:
            kind, name = parameter.rsplit(' ', 1)
            self.assertTrue(any(f.endswith(' ' + name[3:] + ';') and kind.endswith('.' + f.split()[1]) for f in NATIVE_FIELDS), parameter)

    def test_contact_address_fields_and_saved_data_untouched(self):
        location = (SCRIPTS / 'CommissaryLocation.cs').read_text(encoding='utf-8')
        contacts = (SCRIPTS / 'ModContacts.cs').read_text(encoding='utf-8')
        commissary = (SCRIPTS / 'Commissary.cs').read_text(encoding='utf-8')
        for source in (location, contacts, commissary):
            for field in ('streetName =', 'streetNumber =', '.Address =', 'BusinessName ='):
                self.assertNotIn(field, source)
        # The postfix leaves the game's call button (callDialogTypeOverride) alone.
        self.assertNotIn('callButton', method('ModContacts.cs', 'internal static void HeaderPostfix('))
        self.assertNotIn('MarkChanged', location)

    def test_texts_say_where_with_new_keys(self):
        commissary = (SCRIPTS / 'Commissary.cs').read_text(encoding='utf-8')
        unlock = method('Commissary.cs', 'internal static bool Unlock(')
        self.assertIn('CommissaryLocation.MessageKey("foodtrucks:message_commissary_welcome_phone", address)', unlock)
        self.assertIn('welcome["address"] = address;', unlock)
        apply = method('Commissary.cs', 'internal static string? ApplySpace(')
        self.assertIn('key = CommissaryLocation.MessageKey(key, address);', apply)
        self.assertIn('data["address"] = address;', apply)
        # Rent and move say where; a release keeps its key (en.json has no released_address).
        self.assertIn('var address = choice.Op == "release" || site == null ? null : CommissaryLocation.Address(site);', apply)
        space_keys = set(re.findall(r'"(foodtrucks:message_commissary_space_\w+)"', apply))
        self.assertEqual(space_keys, {f'foodtrucks:message_commissary_space_{op}' for op in ('rented', 'moved', 'released')})
        for key in space_keys - {'foodtrucks:message_commissary_space_released'}:
            self.assertIn(key + '_address', NEW_KEYS)
        self.assertNotIn('foodtrucks:message_commissary_space_released_address', NEW_KEYS)
        start = method('Commissary.cs', 'private static DialogEntry Start()')
        for key in ('foodtrucks:dialog_commissary_start_spaces', 'foodtrucks:dialog_commissary_start_standing'):
            self.assertIn(f'CommissaryLocation.MessageKey("{key}", address)', start)
        self.assertEqual(start.count('address = address ?? ""'), 2)
        self.assertIn('CommissaryLocation.Address(site!)', start)
        self.assertIn('internal static string MessageKey(string key, string? address) => address == null ? key : key + "_address";',
                      (SCRIPTS / 'CommissaryLocation.cs').read_text(encoding='utf-8'))
        self.assertNotIn('ba:street_', commissary[commissary.index('internal static bool Unlock('):])

    def test_new_locale_keys_in_every_shipped_locale(self):
        locales = sorted((ROOT / 'unity/FoodTrucks/Locales').glob('*.json'))
        self.assertTrue(locales)
        for path in locales:
            table = json.loads(path.read_text(encoding='utf-8'))
            for key, base in NEW_KEYS.items():
                self.assertIn(key, table, f'{path.name}: {key}')
                self.assertTrue(table[key].strip(), f'{path.name}: {key}')
                if base is None:
                    continue
                # Older messages keep their key and data (no address) and still render in full.
                self.assertNotIn('{address}', table[base], f'{path.name}: {base}')
                self.assertEqual(placeholders(table[key]), placeholders(table[base]) | {'address'}, f'{path.name}: {key}')
            self.assertNotIn('foodtrucks:message_commissary_space_released_address', table, path.name)
            self.assertNotIn('GPS', table['foodtrucks:bizman_page_space_bay_hint'])
            self.assertIn('beside', table['foodtrucks:bizman_page_space_bay_hint'])

    def test_native_names_in_the_game_assembly(self):
        # Optional: re-reads the names above from a local copy of the game's assembly (never shipped or committed).
        folder = os.environ.get('BA_GAME_DLLS')
        ilspy = shutil.which('ilspycmd')
        if not folder or not (Path(folder) / 'BigAmbitions.dll').is_file() or not ilspy:
            self.skipTest('BA_GAME_DLLS with BigAmbitions.dll and ilspycmd needed')
        views = {}
        for kind in (NATIVE_TYPE, 'CityMap'):
            result = subprocess.run([ilspy, '--disable-updatecheck', '-t', kind, str(Path(folder) / 'BigAmbitions.dll')],
                                    capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            views[kind] = result.stdout
        contacts = views[NATIVE_TYPE]
        self.assertIn(NATIVE_METHOD, contacts)
        for field in NATIVE_FIELDS:
            self.assertIn(field, contacts)
        # The address-undefined branch the postfix follows: address and View on map hidden, the call by override.
        self.assertIn('if (contact.Address.IsUndefined())', contacts)
        self.assertIn('contact.callDialogTypeOverride != CallDialogType.NotImplemented', contacts)
        self.assertIn('addressLabel.SetValue(contact.Address.ToFormattedString(), clearKey: true);', contacts)
        self.assertIn('InstanceBehavior<CityManager>.Instance.cityMap.buildingToFocus = cityBuildingController;', contacts)
        self.assertIn('InstanceBehavior<UIs>.Instance.fullMenu.Toggle(show: false);', contacts)
        for member in NATIVE_MAP:
            self.assertIn(member, views['CityMap'])
