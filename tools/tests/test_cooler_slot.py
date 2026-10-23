"""Commissary transfer mock must expose the same dedicated-fridge rule as Core."""
from pathlib import Path
import sys
import tempfile
import unittest
TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
sys.path.insert(0, str(TOOLS / 'tests'))
from offline_transport import NAMESPACE

class CoolerSlotTests(unittest.TestCase):
    def test_full_kitchen_accepts_only_one_dedicated_fridge(self):
        for kind, slots in [('cart', 2), ('truck', 4)]:
            for fridge in ['ba:itemname_drinksfridge', 'ba:itemname_largedrinksfridge']:
                with self.subTest(kind=kind, fridge=fridge), tempfile.TemporaryDirectory() as folder:
                    mock = NAMESPACE['Mock'](folder, clock_rate=0)
                    try:
                        v = mock.spawn(kind)
                        v['commissary'] = {'bay_site': 'hells-kitchen'}
                        v['pieces'] = [{'item': 'hotdoggrill', 'units': 0, 'cap': 40} for _ in range(slots)]
                        stock = [{'site': 'hells-kitchen', 'key': fridge, 'item': 'Drinks fridge', 'amount': 2},
                                 {'site': 'hells-kitchen', 'key': 'ba:itemname_industrialcoffeemachine', 'item': 'Coffee', 'amount': 1}]
                        mock.state['commissary']['stock'] = stock
                        self.assertIsNone(mock.transfer(v, 'take', fridge, 1))
                        self.assertEqual(slots + 1, len(v['pieces']))
                        self.assertIn('NoSlot', mock.transfer(v, 'take', fridge, 1))
                        self.assertIn('NoSlot', mock.transfer(v, 'take', stock[1]['key'], 1))
                        self.assertEqual(1, stock[0]['amount'])
                    finally:
                        mock.unload()
