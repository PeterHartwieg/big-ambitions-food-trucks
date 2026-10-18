import copy
from pathlib import Path
import runpy
import sys
import tempfile
import unittest

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
sys.path.insert(0, str(TOOLS / 'tests'))
from ftlib.bridge import BridgeError
from offline_transport import OfflineSession

PLAY = runpy.run_path(str(TOOLS / 'play'), run_name='hands_test')['main']


class HandsTests(unittest.TestCase):
    def setUp(self):
        self.session = OfflineSession().__enter__()
        self.addCleanup(self.session.__exit__, None, None, None)
        self.mock, self.bridge = self.session.mock, self.session.bridge
        self.mock.state['save'] = 'FT playtest hands'
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.stop = str(Path(self.folder.name) / 'STOP')
        self.lines = []
        self.flat = 'ba:street_thirdstreet#9'
        self.bed = {'key': 'ba:itemname_kingsizebed', 'item': 'King size bed', 'amount': 1, 'paid': True,
                    'food': False, 'furniture': True, 'price': 912345.67}
        self.box = {'key': 'ba:itemname_closedcardboardbox', 'item': 'Closed cardboard box', 'contents': [self.bed]}
        self.truck = {'id': 'handtruck-test', 'name': 'Hand truck', 'kind': 'handtruck', 'inside': self.flat,
                      'x': 0., 'y': 0., 'z': 0., 'contents': [], 'capacity': 4}
        self.mock.life.update(inside=self.flat, homes=[self.flat], hands=copy.deepcopy(self.box), handtrucks=[self.truck])

    def call(self, cmd, **args):
        return self.bridge.call(cmd, args)['result']

    def play(self, *args):
        self.lines.clear()
        return PLAY(list(args), bridge=self.bridge, out=self.lines.append, stop_file=self.stop,
                    memory=str(Path(self.folder.name) / 'memory'))

    def test_drop_keeps_bed_boxed_pickup_restores_it_and_blocked_floor_loses_nothing(self):
        self.mock.life['drop_blocked'] = True
        self.assertEqual(self.play('drop', '2', '3'), 0, self.lines)
        self.assertIn('Not put down', self.lines[0])
        self.assertEqual(self.mock.life['hands'], self.box)
        self.assertFalse(self.mock.life['dropped'])
        self.mock.life['drop_blocked'] = False
        self.assertEqual(self.play('drop', '2', '3'), 0, self.lines)
        self.assertIsNone(self.mock.life['hands'])
        self.assertFalse(self.mock.life['beds'])
        things = self.call('use')['things']
        ident = next(t['id'] for t in things if t['kind'] == 'DroppedContainer')
        self.assertEqual(self.play('pickup', str(ident)), 0, self.lines)
        self.assertEqual(self.mock.life['hands'], self.box)
        self.assertFalse(self.mock.life['dropped'])
        with self.assertRaisesRegex(BridgeError, 'list again'):
            self.call('pickup', id=ident)

    def test_partial_load_capacity_release_regrab_and_take_preserve_all_cargo(self):
        self.truck['capacity'] = 0
        self.assertEqual(self.play('handtruck', self.truck['id'], 'grab'), 0, self.lines)
        self.assertIn("couldn't take everything", self.lines[0])
        self.assertIsNone(self.mock.state['player']['in_vehicle'])
        self.assertEqual(self.mock.life['hands'], self.box)
        self.truck['capacity'] = 1
        second = {**self.bed, 'key': 'ba:itemname_chair', 'item': 'Chair'}
        self.mock.life['hands']['contents'].append(second)
        self.assertFalse(self.call_unpaused('handtruck', op='load', id=self.truck['id'])['done'])
        self.assertEqual(self.mock.life['hands']['contents'], [second])
        self.assertEqual(self.truck['contents'], [self.bed])
        self.truck['capacity'] = 2
        self.assertEqual(self.play('handtruck', self.truck['id'], 'grab'), 0, self.lines)
        self.assertIsNone(self.mock.life['hands'])
        self.assertEqual(self.play('look'), 0, self.lines)
        self.assertTrue(any('pushing Hand truck' in line for line in self.lines))
        self.assertTrue(any('King size bed' in line and 'Chair' in line for line in self.lines))
        self.assertFalse(any('912345' in line or 'price' in line for line in self.lines))
        self.assertEqual(self.play('handtruck', 'release'), 0, self.lines)
        self.assertIsNone(self.mock.state['player']['in_vehicle'])
        self.assertEqual(self.truck['contents'], [self.bed, second])
        self.assertEqual(self.play('handtruck', self.truck['id'], 'grab'), 0, self.lines)
        self.assertEqual(self.play('handtruck', self.truck['id'], 'take', self.bed['key']), 0, self.lines)
        self.assertIsNone(self.mock.state['player']['in_vehicle'])
        self.assertEqual(self.mock.life['hands'], self.box)
        self.assertEqual(self.truck['contents'], [second])
        self.assertFalse(any('912345' in line for line in self.lines))
        self.assertTrue(self.mock.state['clock']['paused'])

    def call_unpaused(self, cmd, **args):
        self.call('time', speed=1)
        try:
            return self.call(cmd, **args)
        finally:
            self.call('time', pause=True)

    def outside_home(self):
        x, z = self.mock.door(self.flat)
        self.mock.life['inside'] = None
        self.mock.state['player'].update(x=x, z=z)
        return next(t['id'] for t in self.call('use')['things'] if t.get('address') == self.flat)

    def test_home_entry_keeps_paid_bag_box_and_unpaid_flags_without_stowing(self):
        bag = {'key': 'ba:itemname_paperbag', 'item': 'Paper bag', 'contents': [
            {'key': 'ba:itemname_banana', 'item': 'Banana bag', 'amount': 5, 'paid': True, 'food': True}]}
        for hands in (bag, self.box, {**bag, 'key': 'ba:itemname_shoppingbasket'},
                      {**bag, 'contents': [{**bag['contents'][0], 'paid': False}]}):
            with self.subTest(container=hands['key'], paid=hands['contents'][0]['paid']):
                self.mock.life['hands'] = copy.deepcopy(hands)
                money = self.mock.state['money']
                ident = self.outside_home()
                result = self.call_unpaused('use', op='click', id=ident)
                self.assertTrue(result['handled'])
                self.assertIsNone(result['why'])
                self.assertEqual(result['inside'], self.flat)
                self.assertEqual(self.mock.life['hands'], hands)
                self.assertEqual(self.mock.state['money'], money)

    def test_home_use_reports_blockers_and_native_route_refusal_without_losing_bag(self):
        ident = self.outside_home()
        self.mock.life['hands'] = {'key': 'ba:itemname_paperbag', 'item': 'Paper bag', 'contents': [
            {'key': 'ba:itemname_banana', 'item': 'Banana bag', 'amount': 5, 'paid': True, 'food': True}]}
        hands = copy.deepcopy(self.mock.life['hands'])
        self.mock.life['use_navigation_blockers'] = ['Map']
        self.assertEqual(self.play('use', str(ident)), 0, self.lines)
        self.assertTrue(any('navigation blocked by Map' in line for line in self.lines), self.lines)
        self.assertFalse(any('nothing happened' in line for line in self.lines))
        self.assertTrue(self.mock.state['clock']['paused'])
        self.mock.life['use_navigation_blockers'] = []
        for field, value, expected in [('use_on_navmesh', False, 'Leave the motor vehicle'),
                                        ('use_route', 'missing', "Can't reach item"),
                                        ('use_route', 'partial', "Can't reach item")]:
            with self.subTest(field=field, value=value):
                self.mock.life[field] = value
                result = self.call_unpaused('use', op='click', id=ident)
                self.assertFalse(result['handled'])
                self.assertIn(expected, result['why'])
                self.assertIsNone(result['inside'])
                self.assertEqual(self.mock.life['hands'], hands)
                self.mock.life.update(use_on_navmesh=True, use_route='complete')

    def test_building_use_keeps_private_property_and_full_menu_refusals(self):
        ident = self.outside_home()
        self.mock.dialog = 'phone_app'
        self.mock.state['clock']['paused'] = False
        result = self.call('use', op='click', id=ident)
        self.assertFalse(result['handled'])
        self.assertIn('Close the phone or map', result['why'])
        self.mock.dialog = 'closed'
        self.call('time', pause=True)
        self.mock.life['homes'] = []
        self.mock.last_use[ident - 1]['for_rent'] = False  # A residential rented by someone else.
        result = self.call_unpaused('use', op='click', id=ident)
        self.assertTrue(result['handled'])  # Handled doesn't mean the game entered the building.
        self.assertIsNone(result['inside'])
        self.assertIn('Private property.', result['why'])
        self.assertEqual(result['notices'], [result['why']])

    def test_release_frees_hands_for_cashier_food_then_recovers_bed(self):
        city = runpy.run_path(str(TOOLS / 'mock-bridge'), run_name='hands_city')['CITY']
        shop = next(b['key'] for b in city if b.get('businessType') == 'ba:businesstype_fastfoodrestaurant')
        self.mock.life['inside'] = shop
        self.truck['inside'] = shop
        def use_till():
            ident = next(t['id'] for t in self.call('use')['things'] if t['name'] == 'Cash register')
            self.assertEqual(self.play('use', str(ident)), 0, self.lines)
        use_till()
        self.assertTrue(any('empty hands' in line for line in self.lines))
        self.assertEqual(self.play('handtruck', self.truck['id'], 'grab'), 0, self.lines)
        use_till()
        self.assertTrue(any('empty hands' in line for line in self.lines))
        self.assertEqual(self.play('handtruck', 'release'), 0, self.lines)
        use_till()
        buttons = self.call('ui', op='list')['buttons']
        product = next(b['text'] for b in buttons if b['kind'] == 'option')
        self.assertEqual(self.play('ui', 'click', product), 0, self.lines)
        self.assertEqual(self.play('ui', 'click', 'Place order'), 0, self.lines)
        self.mock.state['player']['needs']['hunger'] = 10
        self.assertEqual(self.play('eat'), 0, self.lines)
        self.assertEqual(self.play('handtruck', self.truck['id'], 'take', 'King size bed'), 0, self.lines)
        self.assertEqual(self.mock.life['hands'], self.box)
        self.assertFalse(self.truck['contents'])
        self.assertEqual(self.play('drop'), 1, self.lines)
        self.assertIn('only inside a building you can edit', self.lines[0])

    def test_readonly_list_and_mutation_guards_and_unreachable_truck(self):
        self.mock.state['save'] = 'Personal'
        self.assertEqual(len(self.call('handtruck')['handtrucks']), 1)
        for cmd, args in [('drop', {}), ('pickup', {'id': 1}), ('handtruck', {'op': 'grab', 'id': self.truck['id']})]:
            with self.subTest(cmd=cmd), self.assertRaisesRegex(BridgeError, "doesn't start with FT"):
                self.call(cmd, **args)
        self.mock.state['save'] = 'FT playtest hands'
        with self.assertRaisesRegex(BridgeError, 'paused'):
            self.call('handtruck', op='grab', id=self.truck['id'])
        self.truck['y'] = 6
        self.assertEqual(self.call('handtruck')['handtrucks'], [])
        with self.assertRaisesRegex(BridgeError, "isn't near you"):
            self.call_unpaused('handtruck', op='grab', id=self.truck['id'])
        self.truck['y'] = 0
        self.mock.life['hands']['key'] = 'ba:itemname_shoppingbasket'
        with self.assertRaisesRegex(BridgeError, 'shopping basket'):
            self.call_unpaused('handtruck', op='grab', id=self.truck['id'])
        Path(self.stop).write_text('The referee stopped this run.')
        for argv in [('drop',), ('pickup', '1'), ('handtruck', 'release')]:
            self.assertEqual(self.play(*argv), 2, self.lines)

    def test_bag_stays_a_bag_and_keeps_its_contents_through_storage_and_drop(self):
        bag = {'key': 'ba:itemname_paperbag', 'item': 'Paper bag', 'contents': [
            {'key': 'ba:itemname_burger', 'item': 'Burger', 'amount': 2, 'paid': True, 'food': True, 'saturation': 20}]}
        self.mock.life['hands'] = copy.deepcopy(bag)
        self.assertEqual(self.play('handtruck', self.truck['id'], 'load'), 0, self.lines)
        self.assertEqual(self.play('handtruck', self.truck['id'], 'take', 'Paper bag'), 0, self.lines)
        self.assertEqual(self.mock.life['hands'], bag)
        self.assertEqual(self.play('drop'), 0, self.lines)
        ident = next(t['id'] for t in self.call('use')['things'] if t['kind'] == 'DroppedContainer')
        self.assertEqual(self.play('pickup', str(ident)), 0, self.lines)
        self.assertEqual(self.mock.life['hands'], bag)

    def test_load_and_grab_refuse_any_unpaid_box_or_bag_contents_without_changes(self):
        for key, name in [('ba:itemname_closedcardboardbox', 'Closed cardboard box'),
                          ('ba:itemname_paperbag', 'Paper bag')]:
            for op in ('load', 'grab'):
                with self.subTest(container=name, op=op):
                    self.mock.life['hands'] = {'key': key, 'item': name, 'contents': [
                        {'key': 'ba:itemname_bottledwater', 'item': 'Bottled water', 'amount': 1, 'paid': True, 'food': True},
                        {'key': 'ba:itemname_burger', 'item': 'Burger', 'amount': 2, 'paid': False, 'food': True}]}
                    self.truck['contents'] = []
                    self.mock.state['player']['in_vehicle'] = None
                    before = copy.deepcopy(self.mock.life['hands'])
                    with self.assertRaisesRegex(BridgeError, 'Pay for it first: Burger'):
                        self.call_unpaused('handtruck', op=op, id=self.truck['id'])
                    self.assertEqual(self.mock.life['hands'], before)
                    self.assertEqual(self.truck['contents'], [])
                    self.assertIsNone(self.mock.state['player']['in_vehicle'])
                    self.mock.life['hands']['contents'][1]['paid'] = True
                    self.assertTrue(self.call_unpaused('handtruck', op=op, id=self.truck['id'])['done'])
                    self.assertIsNone(self.mock.life['hands'])
                    self.assertEqual(self.mock.state['player']['in_vehicle'], self.truck['id'] if op == 'grab' else None)

    def test_game_bindings_use_native_controls_without_cargo_or_position_writes(self):
        source = (TOOLS.parent / 'unity/FoodTrucks/Scripts/TestBridgeHands.cs').read_text()
        life = (TOOLS.parent / 'unity/FoodTrucks/Scripts/TestBridgeLife.cs').read_text()
        for binding in ('ui.itemPanelUI.ClickPark()', 'truck.DriveVehicle()', 'truck.MoveAndAddHeldItemToStorage()',
                        'truck.MoveAndManageStorage()', 'button.onClick.Invoke()', 'return UseClick(args)'):
            self.assertIn(binding, source)
        self.assertIn('if (keepContainer) panel.ClickPlaceBox()', life)
        self.assertNotRegex(source, r'ActiveVehicleId\s*=(?!=)')
        for shortcut in ('ItemInstanceInHands =', 'TryToAddToCargo(', 'RemoveFromCargo(', 'transform.position =', '[HarmonyPatch'):
            self.assertNotIn(shortcut, source)


if __name__ == '__main__':
    unittest.main()
