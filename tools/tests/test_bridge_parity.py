import copy
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

    def test_bay_candidates_shape_and_guards(self):
        reply=self.mock.call('commissary',{'op':'bay_candidates','site':'murray-hill','radius':80})
        self.assertEqual(reply['max_metres'],30)
        origin=reply['origins'][0]
        self.assertEqual(set(origin),{'kind','x','z','door','walk_start','parking_colliders','truncated','candidates'})
        self.assertEqual(set(origin['candidates'][0]),{'x','z','heading','distance','walk','walk_m','straight_m','automatic','rejected'})
        self.assertEqual(sum(c['automatic'] for c in origin['candidates']),1)
        self.assertEqual(len(self.mock.call('commissary',{'op':'bay_candidates','site':'murray-hill','radius':30})['origins'][0]['candidates']),2)
        self.refused('commissary',{'op':'bay_candidates','site':'murray-hill','radius':81},'radius is 5 to 80 metres.')
        self.refused('commissary',{'op':'nope','site':'murray-hill'},"Unknown commissary op 'nope': order, bay_candidates.")

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
        self.mock.call('time',{'pause':False})
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
            text='ui works only in a loaded FT city; use load from the main menu.' if cmd=='ui' else f"{guard} changes the save, and the loaded save 'Personal' doesn't start with FT: load an FT save first."
            self.refused(cmd,args,text)
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
        self.assertEqual(pose,{'x':1.23,'y':0,'z':2.35,'heading':270,'surface':"the only ground surface, walkable; walking routes checked from the player's place"})
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

    # Live session 4 follow-ups (13 October 2026): needs in state, keep_needs on run_until, the teleport surface, the
    # free camera and the shot's camera in the reply, health's time_skip.
    def test_needs_in_state_and_kept_up_on_run_until(self):
        needs=self.mock.call('state',{})['player']['needs']
        self.assertEqual(set(needs),{'energy','hunger','happiness','happiness_modifiers','max','collapse_at','energy_off','going_to_hospital','kept_up'})
        self.refused('time',{'speed':2,'keep_needs':'yes'},'keep_needs must be true or false.')
        self.assertTrue(self.mock.call('time',{'run_until':{'day':'+2','time':'07:00'},'speed':20})['keep_needs'])
        self.mock.state['player']['needs']['energy']=39.5
        self.mock.tick()
        self.assertEqual(self.mock.state['player']['needs']['energy'],100.)
        self.assertTrue(any(e['text'].startswith('Test bridge: needs topped up while the clock runs (energy 39.4 to 100.0') for e in self.mock.ring))
        self.assertFalse(self.mock.call('time',{'run_until':{'day':'+2','time':'07:00'},'keep_needs':False})['keep_needs'])
        self.assertFalse(self.mock.call('time',{'speed':4})['keep_needs'])
        self.assertTrue(self.mock.call('time',{'speed':4,'keep_needs':True})['keep_needs'])
        self.assertFalse(self.mock.call('time',{'pause':True,'keep_needs':True})['keep_needs'])

    def test_refused_time_request_changes_nothing(self):
        self.mock.call('time',{'run_until':{'day':'+1','time':'09:00'},'speed':10})
        before=copy.deepcopy(self.mock.state['clock']); until=self.mock.until
        for args,text in [({'run_until':{'time':'25:00'}},'run_until.time must be "HH:MM".'),
                          ({'run_until':{'day':'+0','time':'06:00'}},'day 1 06:00 has passed already: it is day 1 07:00.'),
                          ({'run_until':{'day':'soon','time':'08:00'},'keep_needs':False},'run_until.day must be a day number or "+N".')]:
            self.refused('time',args,text)
            self.assertEqual(self.mock.state['clock'],before)
            self.assertEqual(self.mock.until,until)
        self.mock.call('time',{'pause':True})
        self.refused('time',{'run_until':{'time':'25:00'}},'run_until.time must be "HH:MM".')
        self.assertFalse(self.mock.state['clock']['keep_needs'])
        self.assertIsNone(self.mock.state['clock']['run_until'])

    def test_needs_top_ups_stop_outside_an_ft_save(self):
        self.mock.call('time',{'run_until':{'day':'+2','time':'07:00'},'speed':20})
        self.mock.state['save']='Renamed'
        self.mock.state['player']['needs']['energy']=10.
        self.mock.tick()
        self.assertFalse(self.mock.state['clock']['keep_needs'])
        self.assertLess(self.mock.state['player']['needs']['energy'],10.)
        self.assertTrue(any(e['text']=="Test bridge: needs top-ups stopped: the save is now 'Renamed', which doesn't start with FT." for e in self.mock.ring))

    def test_teleport_reports_the_surface_and_a_vehicle_the_recheck(self):
        reply=self.mock.call('teleport',{'who':'player','x':5,'z':6,'y':0.5})
        self.assertEqual(reply['surface'],"the walkable surface nearest the y asked for (0.50) of 1: 0.00 walkable; walking routes checked from the player's place")
        # Live session 5, row 40: a y with no walkable ground near it is refused, not snapped to the street.
        self.refused('teleport',{'who':'player','x':5,'z':6,'y':5.06},
                     "No walkable ground at x 5.0, z 6.0 near y 5.06: no walkable surface within 1.5 m of the y asked for (5.06); surfaces there: 0.00 walkable; walking routes checked from the player's place.")
        reply=self.mock.call('teleport',{'who':self.id,'x':5,'z':6})
        self.assertEqual(reply['in_the_way_after'],[])
        self.assertNotIn('surface',self.mock.call('teleport',{'who':'player','place':'vehicle:'+self.id}))
        far=self.mock.call('teleport',{'who':'player','x':500,'z':500})
        self.assertTrue(far['surface'].endswith('; walking routes not checked (more than 100 m from the player)'))

    def test_free_camera_and_shot_reply(self):
        reply=self.mock.call('shoot',{'name':'free','x':0,'y':1.65,'z':0,'look_at':{'x':10,'y':1.65,'z':0},'width':16,'height':16})
        self.assertEqual(reply['camera'],{'x':0,'y':1.65,'z':0,'yaw':90.,'pitch':0.,'fov':60})
        self.assertIsNone(reply['pose_note'])
        self.refused('shoot',{'name':'both','x':0,'y':1,'z':0,'yaw':3,'look_at':{'x':1,'y':1,'z':1}},'shoot takes "look_at" or "yaw" and "pitch", not both.')
        self.refused('shoot',{'name':'same','x':0,'y':1,'z':0,'look_at':{'x':0,'y':1,'z':0}},'look_at is where the camera stands; give a point away from it.')
        self.refused('shoot',{'name':'bad','x':0,'y':1,'z':0,'look_at':{'x':0}},'look_at needs "x", "y" and "z".')
        self.refused('shoot',{'name':'wide','x':0,'y':1,'z':0,'fov':150},'fov must be 5 to 120 degrees.')
        self.assertEqual(self.mock.call('shoot',{'name':'pose','pose':'vehicle-side','target':self.id,'width':16,'height':16})['pose_note'],None)

    def test_shot_diagnostics_compare_and_game_view(self):
        # Live session 6 research: the bridge lists what it uncovered (the game's view-blocking fade) and the outlines it
        # lifted, and can write the as-is picture and the game's own frame beside the shot.
        reply=self.mock.call('shoot',{'name':'diag','x':0,'y':1.7,'z':0,'yaw':0,'width':16,'height':16})
        self.assertEqual((reply['uncovered'],reply['outlined'],reply['hovered'],reply['asis_path'],reply['game_path'],reply['game_error']),([],[],[],None,None,None))
        self.assertNotIn('asis_uncovered',reply)
        reply=self.mock.call('shoot',{'name':'diag','x':0,'y':1.7,'z':0,'yaw':0,'width':16,'height':16,'compare':True,'game_view':True})
        self.assertTrue(reply['asis_path'].endswith('diag-asis.png'))
        self.assertTrue(reply['game_path'].endswith('diag-game.png'))
        self.assertEqual((reply['asis_uncovered'],reply['asis_outlined'],reply['asis_hovered']),([],[],[]))
        self.refused('shoot',{'name':'diag','x':0,'y':1,'z':0,'frames':4},'frames must be 8 to 240.')
        self.assertTrue(self.mock.call('shoot',{'name':'diag','x':0,'y':1,'z':0,'frames':60,'width':16,'height':16})['path'])
        self.refused('shoot',{'name':'diag','x':0,'y':1,'z':0,'compare':'yes'},'compare must be true or false.')
        state=self.mock.call('state',{})
        self.assertEqual(set(state['game_camera']),{'x','y','z','yaw','pitch'})
        self.assertEqual(state['view_blocked'],[])

    def test_health_has_time_skip(self):
        self.assertFalse(self.mock.health()['time_skip'])
        self.mock.time_skip=True
        self.assertTrue(self.mock.health()['time_skip'])


if __name__=='__main__':unittest.main()
