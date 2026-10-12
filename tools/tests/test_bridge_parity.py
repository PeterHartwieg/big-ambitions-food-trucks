import json
from pathlib import Path
import runpy
import sys
import threading
import time
import tempfile
import unittest

TOOLS=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(TOOLS))
from ftlib.scenario import Runner
from ftlib.bridge import BridgeError

NS=runpy.run_path(str(TOOLS/'mock-bridge'),run_name='mock_test')


class ParityTests(unittest.TestCase):
    def setUp(self):
        self.folder=tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.mock=NS['Mock'](self.folder.name)
        self.v=self.mock.spawn('truck')
        self.id=self.v['id']

    def refused(self, cmd, args, text):
        with self.assertRaises(ValueError) as exc:
            self.mock.call(cmd,args)
        self.assertEqual(str(exc.exception),text)

    def test_driving_and_paused_op_errors(self):
        for op,label in [('open','Open here'),('exit','Park'),('rent_bay','Commissary'),('restock','Commissary')]:
            self.refused('vehicle',{'id':self.id,'op':op},f"{label} is on the vehicle's panel, shown while the player drives the truck or pushes the cart: vehicle enter first.")
        self.mock.call('vehicle',{'id':self.id,'op':'enter'})
        self.refused('vehicle',{'id':self.id,'op':'enter'},f'The player is in {self.id} already.')
        self.refused('vehicle',{'id':self.id,'op':'open'},"Open parks with the game's Park button, which doesn't work while paused: unpause first (time).")
        self.refused('vehicle',{'id':self.id,'op':'exit'},"The game's Park button refused: paused, no parking here, or the panel isn't showing.")
        self.mock.call('time',{'pause':False})
        reply=self.mock.call('vehicle',{'id':self.id,'op':'open'})
        self.assertFalse(reply['state']['open'])
        self.assertEqual(reply['notices'],["The truck can't open without any equipment installed."])

    def test_open_teleport_and_hatch_refusals(self):
        self.v['open']=True
        self.refused('teleport',{'who':self.id,'x':1,'z':2},f'Vehicle {self.id} is open for business: close it first (vehicle close).')
        self.refused('vehicle',{'id':self.id,'op':'step_out'},f"The player isn't at the hatch of {self.id}.")
        self.mock.state['player']['carrying']='ba:itemname_hotdog'
        self.refused('vehicle',{'id':self.id,'op':'back_to_hatch'},"Back to the hatch isn't offered now: it needs an open truck and the player on foot, empty-handed and not at a hatch.")
        self.refused('teleport',{'who':self.id,'place':'commissary:hells-kitchen'},'commissary: places the player at the door; a vehicle goes to bay:<site>.')
        self.refused('teleport',{'who':self.id,'place':'vehicle:'+self.id},'vehicle: places the player beside a vehicle.')

    def test_time_target_rules_and_reset(self):
        self.refused('time',{},'time needs "pause", "speed" or "run_until".')
        for speed in (0,-1,61): self.refused('time',{'speed':speed},'speed must be above 0 and at most 60.')
        self.refused('time',{'run_until':{'day':'+0','time':'07:00'}},'day 1 07:00 has passed already: it is day 1 07:00.')
        self.refused('time',{'run_until':{'day':'next','time':'08:00'}},'run_until.day must be a day number or "+N".')
        self.refused('time',{'run_until':{}},'run_until needs "time" ("HH:MM").')
        self.refused('time',{'run_until':{'time':'24:00'}},'run_until.time must be "HH:MM".')
        self.mock.call('time',{'run_until':{'day':'+1','time':'07:01'},'speed':20})
        self.assertEqual(self.mock.until,1440+421)
        self.assertEqual(self.mock.state['clock']['speed'],20)
        for _ in range(1441): self.mock.tick()
        self.assertEqual(self.mock.state['clock']['time'],'07:01')
        self.assertTrue(self.mock.state['clock']['paused'])
        self.assertEqual(self.mock.state['clock']['speed'],1)
        self.mock.call('time',{'speed':4})
        self.assertFalse(self.mock.state['clock']['paused'])
        self.mock.call('time',{'speed':2,'pause':True})
        self.assertTrue(self.mock.state['clock']['paused'])
        self.assertEqual(self.mock.state['clock']['speed'],2)

    def test_entry_and_hatch_walks_finish_after_the_reply(self):
        self.mock.state['player']['x']=100
        self.mock.call('vehicle',{'id':self.id,'op':'enter'})
        self.assertIsNone(self.mock.state['player']['in_vehicle'])
        thread=threading.Thread(target=self.mock.run_clock,daemon=True)
        thread.start()
        try:
            deadline=time.monotonic()+2
            while self.mock.state['player']['in_vehicle']!=self.id and time.monotonic()<deadline: self.mock.stop.wait(.01)
            self.assertEqual(self.mock.state['player']['in_vehicle'],self.id)
            self.mock.state['clock']['paused']=False
            self.mock.call('vehicle',{'id':self.id,'op':'exit'})
            self.v['open']=True
            self.mock.call('vehicle',{'id':self.id,'op':'back_to_hatch'})
            self.assertIsNone(self.mock.state['player']['at_hatch'])
            deadline=time.monotonic()+2
            while self.mock.state['player']['at_hatch']!=self.id and time.monotonic()<deadline: self.mock.stop.wait(.01)
            self.assertEqual(self.mock.state['player']['at_hatch'],self.id)
        finally:
            self.mock.stop.set();thread.join()

    def test_clock_holding_screens_and_escape(self):
        self.refused('ui',{'op':'escape'},'Nothing is open to close (Esc would open the pause menu).')
        self.mock.dialog='commissary'
        held='A screen holds the clock (the phone, the map, a call, an activity or a menu): close it first (ui escape).'
        for args in ({'pause':False},{'speed':4},{'run_until':{'time':'08:00'}}): self.refused('time',args,held)
        self.mock.call('time',{'pause':True,'speed':2})
        self.assertEqual(self.mock.call('ui',{'op':'escape'}),{'closed':'list picker'})
        self.mock.state['player']['at_hatch']=self.id
        self.refused('time',{'pause':False},held)
        self.mock.state['player']['at_hatch']=None
        self.mock.time_skip=True
        self.refused('time',{'pause':True},'A time skip (sleep, a paid activity) is running; wait for it to end.')

    def test_scene_guard_and_null_fields(self):
        self.mock.state['save']='Personal'
        for cmd,args in [('time',{'pause':True}),('money',{'set':1}),('debug',{'action':'hire'}),('vehicle',{'id':self.id,'op':'enter'}),('ui',{'op':'escape'})]:
            guard='ui escape' if cmd=='ui' else cmd
            self.refused(cmd,args,f"{guard} changes the save, and the loaded save 'Personal' doesn't start with FT: load an FT save first.")
        self.mock.call('ui',{'op':'list'})
        self.mock.call('shoot',{'name':'guard','x':0,'y':1,'z':0,'width':16,'height':16})
        self.mock.state.update(scene='menu',save=None)
        for cmd in ('time','money','debug','vehicle','save'):
            self.refused(cmd,{},f'{cmd} works in the city only; the game is at the menu now.')
        state=self.mock.call('state',{})
        for key in ('clock','money','player','licence','commissary','ledger_today'): self.assertIsNone(state[key])
        self.assertEqual(state['vehicles'],[])
        self.assertIsNone(self.mock.health()['day'])
        self.mock.event('at menu')
        self.assertIsNone(self.mock.ring[-1]['day'])
        self.assertIsNone(self.mock.ring[-1]['time'])
        self.mock.call('ui',{'op':'list'})
        self.refused('shoot',{},'shoot works in the city only; the game is at the menu now.')
        self.mock.state['scene']='city'; self.mock.mod_loaded=False
        state=self.mock.call('state',{})
        self.assertIsNotNone(state['clock']);self.assertIsNone(state['licence'])

    def test_new_game_save_load_error_shapes(self):
        for cmd,text in [('new_game',"The bridge starts only games whose name starts with FT, not 'Other'."),
                         ('load',"The bridge loads only saves whose name starts with FT, not 'Other'."),
                         ('save',"The bridge saves only under names that start with FT, not 'Other'.")]:
            self.refused(cmd,{'name':'Other'},text)
        with self.assertRaisesRegex(ValueError,"new_game isn't available:"): self.mock.call('new_game',{'name':'FT new'})
        reply=self.mock.call('load',{'name':'FT bridge'})
        self.assertEqual(reply,{'loading':True,'name':'FT bridge','character':'Mock character'})
        self.refused('load',{'name':'FT bridge'},'The game is loading; wait until /health says menu or city.')

    def test_inclusive_events_and_drops(self):
        since=self.mock.events(0)['next']
        self.mock.event('first')
        self.mock.event('second')
        batch=self.mock.events(since)
        self.assertEqual([e['seq'] for e in batch['events']],[since,since+1])
        self.assertEqual(batch['next'],since+2)
        self.assertEqual(self.mock.events(since+1)['events'][0]['text'],'second')
        self.assertEqual(self.mock.events(batch['next'])['events'],[])
        self.assertEqual(self.mock.events(-5),self.mock.events(1))
        for _ in range(2001): self.mock.event('fill')
        self.assertEqual(self.mock.events(since)['dropped'],3)

    def test_state_differences_and_notice_capture(self):
        self.assertIsNone(self.v['queue']);self.assertIsNone(self.v['route']);self.assertIsNone(self.v['commissary'])
        self.assertIsNone(self.mock.state['licence']['until_day'])
        pose=self.mock.call('teleport',{'who':'player','x':1.234,'z':2.345,'heading':-90})
        self.assertEqual(pose,{'x':1.23,'y':0,'z':2.35,'heading':270})
        self.mock.state['clock']['speed']=1.234
        self.assertEqual(self.mock.call('state',{})['clock']['speed'],1.23)
        cart=self.mock.spawn('cart')
        self.assertIsNone(cart['hatch_side']);self.assertIsNone(cart['hatch_to_pavement'])
        cart['open']=True
        self.mock.state['player']['x']=100
        self.assertEqual(self.mock.call('state',{})['vehicles'][1]['serving'],'nobody')
        self.mock.state['player']['x']=5
        self.assertEqual(self.mock.call('state',{})['vehicles'][1]['serving'],'player')
        self.mock.place_vehicle(self.v,'hells-kitchen')
        self.assertTrue(self.v['legal'])
        self.assertTrue(self.v['legal_reason'].startswith('wheel parking test: legal'))
        self.assertEqual(self.v['commissary'],{'site':None,'in_bay':True,'bay_site':'hells-kitchen'})
        self.mock.selected=self.id
        self.mock.call('debug',{'action':'licence'})
        self.assertEqual(self.mock.state['licence']['until_day'],60)
        self.assertFalse(self.mock.state['licence']['course_done'])
        reply=self.mock.call('debug',{'action':'spawn truck'})
        self.assertEqual(len(reply['notices']),1)
        self.assertNotIn('Notice:',reply['notices'][0])
        self.assertEqual(self.mock.call('debug',{'action':'status'})['notices'],[])
        self.assertEqual(self.mock.saves['FT commissary-pay-on-delivery']['state']['commissary']['orders'],[])

    def test_ui_option_ids_and_selection_confirmation(self):
        self.mock.state['player']['in_vehicle']=self.id
        self.mock.place_vehicle(self.v,'hells-kitchen')
        self.mock.call('vehicle',{'id':self.id,'op':'rent_bay'})
        locale=NS['LOCALE']
        self.mock.call('ui',{'op':'click','text':locale['foodtrucks:itempanel_commissary']})
        listing=self.mock.call('ui',{'op':'list'})
        self.assertEqual(listing['buttons'][0]['kind'],'option')
        order=next(b for b in listing['buttons'] if b['text'].startswith('Order stock for delivery'))
        self.mock.call('ui',{'op':'click','id':order['id']})
        self.assertEqual(self.mock.dialog,'commissary')
        self.mock.call('ui',{'op':'click','text':'Confirm'})
        self.assertEqual(self.mock.dialog,'phone')
        self.refused('ui',{'op':'click','id':order['id']},f'Entry {order["id"]} ({order["text"]}) is no longer on screen; list again.')
        self.mock.dialog='crew'
        self.refused('ui',{'op':'click','text':'Unassigned'},'3 entries read "Unassigned": 1 (Mock/crew/Crew1/option[0]), 2 (Mock/crew/Crew2/option[0]), 3 (Mock/crew/Crew3/option[0]); click by id.')

    def test_relative_place_resolution_and_days_forwarded(self):
        runner=Runner()
        runner.variables['truck']='truck-1'
        self.assertEqual(runner.resolve('vehicle:$truck'),'vehicle:truck-1')
        self.assertEqual(runner.resolve({'run_until':{'day':'+1','time':'08:05'}})['run_until']['day'],'+1')


if __name__=='__main__':unittest.main()
