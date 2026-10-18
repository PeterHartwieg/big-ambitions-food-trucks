import copy
import re
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

    def test_player_and_crew_sales_share_saved_midnight_payment_for_each_vehicle_kind(self):
        for kind in ('cart', 'truck'):
            with self.subTest(kind=kind):
                self.mock.restore('FT hatch-only-selling', emit=False)
                v = self.mock.state['vehicles'][0]
                v.update(kind=kind, open=True)
                self.mock.state['player'].update(x=v['x'], y=v['y'], z=v['z'], at_hatch=v['id'] if kind=='truck' else None)
                self.mock.start_work(v)
                money = self.mock.state['money']
                notices = list(self.mock.recent)
                for _ in range(30): self.mock.tick()
                self.assertEqual(self.mock.state['money'], money)
                self.assertEqual(list(self.mock.recent), notices)
                # Crew serves the same vehicle after the player leaves.
                self.mock.finish_work()
                self.mock.state['player'].update(x=v['x']+200, at_hatch=None)
                v['crew'] = ['Worker']
                for _ in range(10): self.mock.tick()
                self.assertEqual(self.mock.state['money'], money)
                self.assertFalse(self.mock.state['transactions_today'])
                waiting = 4*NS['SALE_CENTS']/100
                self.assertEqual(self.mock.state['ledger_today']['sales_waiting'], waiting)
                self.assertEqual(self.mock.state['ledger_today']['crew_waiting'], waiting)
                before = self.mock.call('day_summary', {'day':1})
                self.assertEqual(before['fleet']['revenue'], waiting)
                v['open'] = False
                self.mock.store('FT unsettled')
                self.mock.takings.clear()
                self.mock.restore('FT unsettled', emit=False)
                self.assertEqual(sum(self.mock.takings.values()), 4)
                for _ in range(1440-self.mock.absolute): self.mock.tick()
                payments = [t for t in self.mock.state['transactions_today'] if t['type']=='foodtrucks:transaction_crew_sales']
                self.assertEqual([(t['amount'], t['day']) for t in payments], [(waiting, 1)])
                self.assertIn(v['name'], payments[0]['label'])
                self.assertEqual(self.mock.call('day_summary', {'day':1})['fleet']['revenue'], before['fleet']['revenue'])
                self.assertEqual(self.mock.state['ledger_today']['sales_waiting'], 0)
                self.mock.settle_takings()
                self.assertEqual(len([t for t in self.mock.state['transactions_today'] if t['type']=='foodtrucks:transaction_crew_sales']), 1)

    def test_removed_vehicle_and_uninstall_refund_current_takings_once_and_retry_failed_payment(self):
        self.mock.takings[(self.id, 1)] = 3
        self.mock.state['vehicles'].remove(self.v)
        money = self.mock.state['money']
        pay = self.mock.pay
        def fail(*args, **kwargs): raise RuntimeError('payment unavailable')
        self.mock.pay = fail
        with self.assertRaises(RuntimeError): self.mock.settle_takings()
        self.assertEqual(sum(self.mock.takings.values()), 3)
        self.mock.pay = pay
        self.mock.settle_takings()
        self.assertEqual(self.mock.state['money'], money+3*NS['SALE_CENTS']/100)
        self.mock.takings[(self.mock.spawn('cart')['id'], 1)] = 2
        self.mock.settle_takings(all=True)
        self.mock.settle_takings(all=True)
        self.assertFalse(self.mock.takings)
        payments = [t for t in self.mock.state['transactions_today'] if t['type']=='foodtrucks:transaction_crew_sales']
        self.assertEqual(len(payments), 2)
        self.assertEqual(sum(t['amount'] for t in payments), 5*NS['SALE_CENTS']/100)

    def test_fixture_logistics_refuses_inside_and_filters_by_warehouse(self):
        self.mock.inside_building=True
        self.refused('fixture',{'name':'logistics'},"The player is inside a building; the game places furniture and vehicles only in buildings the player isn't in. Teleport the player outside first.")
        self.mock.inside_building=False
        reply=self.mock.call('fixture',{'name':'logistics'})
        self.assertTrue(reply['complete'])
        self.assertEqual([s['step'] for s in reply['steps']],list(NS['Mock'].FIXTURE_STEPS))
        address=reply['warehouse']['address']
        self.assertEqual(len(self.mock.call('logistics',{'op':'status','warehouse':address})['plans']),1)
        self.assertEqual(self.mock.call('logistics',{'op':'status','warehouse':'ba:street_none#1'})['plans'],[])
        again=self.mock.call('fixture',{'name':'logistics'})
        self.assertTrue(all(s['status']=='already' for s in again['steps']))

    def test_bay_candidates_shape_and_guards(self):
        reply=self.mock.call('commissary',{'op':'bay_candidates','site':'lower-manhattan','radius':80})
        self.assertEqual(reply['max_metres'],30)
        origin=reply['origins'][0]
        self.assertEqual(set(reply),{'site','address','status','radius','max_metres','origins'})
        self.assertEqual((reply['site'],reply['address']),('lower-manhattan',None))
        self.assertEqual((origin['kind'],origin['walk_start'],origin['same_side']),('entrance','door',None))
        self.assertEqual(set(origin),{'kind','x','z','door','walk_start','same_side','parking_colliders','truncated','candidates'})
        self.assertEqual(set(origin['candidates'][0]),{'x','z','heading','distance','walk','walk_m','straight_m','same_side','automatic','rejected'})
        self.assertEqual(sum(c['automatic'] for c in origin['candidates']),1)
        across=[c for c in origin['candidates'] if c['same_side'] is False]
        self.assertEqual([c['rejected'] for c in across],['across the road from the door'])
        self.assertTrue(all(c['same_side'] for c in origin['candidates'] if c['automatic']))
        self.assertEqual(len(self.mock.call('commissary',{'op':'bay_candidates','site':'lower-manhattan','radius':30})['origins'][0]['candidates']),3)
        self.refused('commissary',{'op':'bay_candidates','site':'murray-hill','radius':81},'radius is 5 to 80 metres.')

    def test_parks_shape_and_city_only(self):
        reply=self.mock.call('parks',{})
        self.assertEqual(reply['entrance_radius'],150)
        self.assertEqual({p['kind'] for p in reply['parks']},{'parkarea','skatepark'})
        for park in reply['parks']:
            self.assertEqual(set(park),{'name','kind','neighbourhood','colliders','concave_colliders','bounds','centre','entrances'})
            self.assertTrue(park['neighbourhood'].startswith('ba:neighborhood_'))
            for axis in 'xyz': self.assertLessEqual(park['bounds']['min'][axis],park['bounds']['max'][axis])
            distances=[e['distance'] for e in park['entrances']]
            self.assertTrue(0<len(distances)<=5)
            self.assertEqual(distances,sorted(distances))
            self.assertTrue(all(d<=150 for d in distances))
            for e in park['entrances']: self.assertRegex(e['address'],r'^ba:street_[a-z0-9]+#[0-9]+$')
        self.assertEqual(set(reply['skipped'][0]),{'name','kind','reason'})
        self.mock.state['scene']='menu'
        self.refused('parks',{},'parks works in the city only; the game is at the menu now.')

    def test_bay_candidates_from_a_measured_bay(self):
        for site in ('garment-district','murray-hill'):
            origin=self.mock.call('commissary',{'op':'bay_candidates','site':site})['origins'][0]
            self.assertEqual(origin['kind'],'data')
            self.assertTrue(origin['walk_start'].startswith('none (a point set in data is trusted for its walk'))
            self.assertIs(origin['same_side'],True)
            self.assertTrue(all(c['walk'] is None for c in origin['candidates']))
            self.assertEqual(sum(c['automatic'] for c in origin['candidates']),1)
        for origin in [self.mock.call('commissary',{'op':'bay_candidates','site':site})['origins'][0] for site in ('garment-district','murray-hill')]:
            self.assertNotIn('too long a walk from the door',{c['rejected'] for c in origin['candidates']})
        self.mock.measured_across.add('murray-hill')
        origin=self.mock.call('commissary',{'op':'bay_candidates','site':'murray-hill','radius':30})['origins'][0]
        self.assertIs(origin['same_side'],False)
        self.assertFalse(any(c['automatic'] for c in origin['candidates']))
        self.assertEqual({c['rejected'] for c in origin['candidates']},
                         {'the point set in data is across the road from the door, so the automatic search takes nothing from it'})

    def test_bay_candidates_for_a_nose_in_lot_bay(self):
        reply=self.mock.call('commissary',{'op':'bay_candidates','site':'industry-city','radius':30})
        origin=reply['origins'][0]
        self.assertEqual((origin['kind'],origin['x'],origin['z'],origin['same_side']),('data',-1439.0,-1073.4,True))
        self.assertTrue(origin['candidates'])
        self.assertEqual({c['heading'] for c in origin['candidates']},{180})
        picks=[c for c in origin['candidates'] if c['automatic']]
        self.assertEqual([(p['x'],p['z'],p['distance']) for p in picks],[(-1439.0,-1073.4,0.0)])
        self.assertTrue(all(c['walk'] is None and c['walk_m'] is None for c in origin['candidates']))
        self.assertNotIn('too long a walk from the door',{c['rejected'] for c in origin['candidates']})

    def test_bay_candidates_for_any_building(self):
        reply=self.mock.call('commissary',{'op':'bay_candidates','address':'ba:street_twentyfirststreet#4','radius':40})
        self.assertEqual((reply['site'],reply['address'],reply['status']),(None,'ba:street_twentyfirststreet#4',None))
        self.assertEqual({o['kind'] for o in reply['origins']},{'entrance'})
        self.assertIn('same_side',reply['origins'][0]['candidates'][0])
        both='bay_candidates needs "site" (a commissary\'s id) or "address" (any building, as ba:street_…#N), not both.'
        self.refused('commissary',{'op':'bay_candidates','site':'murray-hill','address':'ba:street_twentyfirststreet#4'},both)
        self.refused('commissary',{'op':'bay_candidates'},both)
        self.refused('commissary',{'op':'bay_candidates','address':'ba:street_twentyfirststreet#999'},
                     'no building at ba:street_twentyfirststreet#999 in this city')
        self.refused('commissary',{'op':'bay_candidates','address':'21st Street 4'},
                     '"address" is a building\'s address as the game writes it, ba:street_<name>#<number>; got \'21st Street 4\'.')
        self.refused('commissary',{'op':'nope','site':'murray-hill'},"Unknown commissary op 'nope': order, bay_candidates.")

    def test_driving_and_paused_op_errors(self):
        for op,label in [('exit','Park'),('restock','Commissary stock'),('transfer','Commissary stock')]:
            self.refused('vehicle',{'id':self.id,'op':op},f"{label} is on the vehicle's panel, shown while the player drives the truck or pushes the cart: vehicle enter first.")
        self.mock.call('vehicle',{'id':self.id,'op':'enter'})
        self.refused('vehicle',{'id':self.id,'op':'enter'},f'The player is in {self.id} already.')
        self.refused('vehicle',{'id':self.id,'op':'open'},f"Open for business is in the parked vehicle's on-foot menu: vehicle exit first (the player is in {self.id}).")
        self.refused('vehicle',{'id':self.id,'op':'exit'},"The game's Park button refused: paused, no parking here, or the panel isn't showing.")
        self.mock.call('time',{'pause':False})
        self.mock.call('vehicle',{'id':self.id,'op':'exit'})
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
        self.refused('new_game',{'name':'FT new'},'new_game works from the main menu only; the game is at the city now.')
        reply=self.mock.call('load',{'name':'FT bridge'})
        self.assertEqual(reply,{'loading':True,'name':'FT bridge','character':'Mock character'})
        self.refused('load',{'name':'FT bridge'},'The game is loading; wait until /health says menu or city.')

    def test_new_game_from_the_menu_saves_a_character_of_its_own(self):
        self.mock.state.update(scene='menu',save=None)
        self.mock.character_creator=True
        self.refused('new_game',{'name':'FT new'},'new_game works from the main menu only; the game is at the character creator now.')
        self.mock.character_creator=False
        self.refused('new_game',{'name':'FT bridge'},"A save named 'FT bridge' exists already; new_game never writes over a save: pick another FT name.")
        self.refused('new_game',{'name':'FT a/b'},"'FT a/b' has characters a save's file name can't hold.")
        reply=self.mock.call('new_game',{'name':'FT new'})
        self.assertEqual(reply,{'loading':True,'name':'FT new','character':'FT Playtest','preset':'difficulty_normal'})
        self.assertEqual(self.mock.load,{'name':'FT new','status':'pending','error':None,'new_game':'main menu'})
        self.refused('new_game',{'name':'FT other'},'new_game works from the main menu only; the game is at the loading now.')
        _,finish=self.mock.pending[-1]
        finish()
        self.assertEqual(self.mock.load,{'name':'FT new','status':'done','error':None,'new_game':'saved'})
        state=self.mock.call('state',{})
        self.assertEqual((state['save'],state['clock']['day'],state['clock']['time'],state['clock']['paused'],state['money']),('FT new',1,'08:00',True,10000.))
        self.assertEqual(state['vehicles'],[])
        self.assertIn('FT new',self.mock.saves)
        self.assertNotEqual(self.mock.saves['FT new']['character'],self.mock.saves['FT bridge']['character'])

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
        # The mod's to-dos: a list per vehicle and the fleet's under licence (the mock makes none).
        state=self.mock.call('state',{})
        self.assertEqual(state['licence']['todos'],[]);self.assertEqual(state['vehicles'][0]['todos'],[])
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
        self.assertEqual(self.mock.call('state',{})['vehicles'][1]['serving'],'nobody')
        self.mock.operate(cart,'work_window')
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

    def test_route_carries_the_week_plan_in_the_bridges_shape(self):
        route=NS['week_route'](510,1440,393.2,217.6)
        self.assertEqual(set(route),{'stops','next','week','shifts','problem'})
        self.assertEqual([d['day'] for d in route['week']],['monday','tuesday','wednesday','thursday','friday','saturday','sunday'])
        self.assertEqual(set(route['week'][0]),{'day','closed','stops','route_day','gaps'})
        self.assertEqual(set(route['week'][0]['stops'][0]),{'open','close','x','z'})
        self.assertEqual(route['week'][0]['route_day'],{'start':510,'end':1440})
        self.assertEqual(route['next'],'stop 1, 08:30 to 00:00 at x 393.2, z 217.6')
        # The game side writes the same keys (TestBridgeState.WeekOf and ShiftsOf).
        source=(TOOLS.parent/'unity/FoodTrucks/Scripts/TestBridgeState.cs').read_text()
        for key in ('week','shifts','problem','day','closed','route_day','gaps','employee','week_hours','wants_min','wants_max'):
            self.assertIn(f'["{key}"]',source)

    def test_ui_option_ids_and_selection_confirmation(self):
        self.mock.state['player']['in_vehicle']=self.id
        self.mock.place_vehicle(self.v,'hells-kitchen')
        self.mock.call('vehicle',{'id':self.id,'op':'rent_bay'})
        locale=NS['LOCALE']
        self.mock.call('ui',{'op':'click','text':locale['foodtrucks:itempanel_commissary_stock']})
        listing=self.mock.call('ui',{'op':'list'})
        self.assertEqual(listing['buttons'][0]['kind'],'option')
        fill=next(b for b in listing['buttons'] if b['text'].startswith('Fill up'))
        self.mock.call('ui',{'op':'click','id':fill['id']})
        self.assertEqual(self.mock.dialog,'commissary')
        self.mock.call('ui',{'op':'click','text':'Confirm'})
        self.assertEqual(self.mock.dialog,'closed')
        self.refused('ui',{'op':'click','id':fill['id']},f'Entry {fill["id"]} ({fill["text"]}) is no longer on screen; list again.')
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
        self.assertEqual(set(needs),{'energy','hunger','happiness','happiness_modifiers','max','collapse_at','energy_off','going_to_hospital','kept_up','warnings'})
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
        self.refused('shoot',{'name':'diag','x':0,'y':1,'z':0,'frames':4},'frames must be a whole number from 8 to 240.')
        self.refused('shoot',{'name':'diag','x':0,'y':1,'z':0,'frames':240.5},'frames must be a whole number from 8 to 240.')
        self.assertTrue(self.mock.call('shoot',{'name':'diag','x':0,'y':1,'z':0,'frames':60,'width':16,'height':16})['path'])
        self.refused('shoot',{'name':'diag','x':0,'y':1,'z':0,'compare':'yes'},'compare must be true or false.')
        state=self.mock.call('state',{})
        self.assertEqual(set(state['game_camera']),{'x','y','z','yaw','pitch'})
        self.assertEqual(state['view_blocked'],[])

    def test_drive_refusals_and_reply_match_the_bridge(self):
        source=(TOOLS.parent/'unity/FoodTrucks/Scripts/TestBridgeDrive.cs').read_text()
        world=(TOOLS.parent/'unity/FoodTrucks/Scripts/TestBridgeWorld.cs').read_text()
        commands=(TOOLS.parent/'unity/FoodTrucks/Scripts/TestBridgeCommands.cs').read_text()
        def refusal(cmd,args,text,fragment,where=source):
            # The mock's whole text, and the C#'s literal piece of it (the rest is interpolated there).
            self.refused(cmd,args,text)
            self.assertIn(fragment,where)
        T=self.id
        self.refused('drive',{'op':'fly'},"Unknown drive op 'fly': start, status or check.")
        self.assertIn("Unknown drive op '{op}': start, status or check.",source)
        self.assertEqual(self.mock.call('drive',{'op':'status'}),{'running':False,'phase':'none'})
        refusal('drive',{'id':T,'place':'bay:hells-kitchen','speed':5},'speed is a whole number from 1 to 3 (times real time).',
                'speed is a whole number from {DriveMinSpeed} to {DriveMaxSpeed} (times real time).')
        refusal('drive',{'id':T,'place':'bay:hells-kitchen'},f"You're not in {T}: get in first (vehicle {T} enter).",
                "You're not in {id}: get in first (vehicle {id} enter).")
        cart=self.mock.spawn('cart')['id']
        refusal('drive',{'id':cart,'place':'bay:hells-kitchen'},f'{cart} is a cart: a cart is pushed (walk), not driven.','{id} is a cart: a cart is pushed (walk), not driven.')
        self.mock.state['player']['in_vehicle']=T
        refusal('drive',{'id':T,'place':'bay:hells-kitchen'},'The game is paused: driving takes real time. Set a speed first.',
                'The game is paused: driving takes real time. Set a speed first.')
        self.mock.state['clock']['paused']=False
        refusal('drive',{'id':T,'place':'nowhere'},"Unknown place 'nowhere': use bay:<site>, address:<address> or stop:<N> (today's route).",
                "Unknown place '{run.Place}': use bay:<site>, address:<address> or stop:<N> (today's route).")
        refusal('drive',{'id':T,'place':'stop:x'},"stop: takes the stop's number on today's route, from 1; got 'x'.","stop: takes the stop's number on today's route, from 1; got '{what}'.")
        refusal('drive',{'id':T,'place':'stop:1'},f"{T} has 0 stops on today's route (Monday); no stop 1.",
                """{vehicle.id} has {stops.Count} stop{(stops.Count == 1 ? "" : "s")} on today's route ({today}); no stop {number}.""")
        self.mock.self_driving=False
        refusal('drive',{'id':T,'place':'bay:hells-kitchen'},"HART. Self Driving isn't loaded: the truck can't drive itself. Switch it on in the Mods menu.",
                "HART. Self Driving isn't loaded: the truck can't drive itself. Switch it on in the Mods menu.")
        self.assertEqual(self.mock.call('drive',{'op':'check'})['problems'],["HART. Self Driving isn't loaded: switch it on in the Mods menu."])
        self.assertIn("HART. Self Driving isn't loaded: switch it on in the Mods menu.",source)
        self.mock.self_driving=True
        started=self.mock.call('drive',{'id':T,'place':'bay:hells-kitchen'})
        self.assertTrue(started['running'])
        keys={'running','phase','id','place','speed','goal','seconds','game_minutes','from','pull_out','attempts','ride_to','ride','park','error','truck','vehicle'}
        self.assertEqual(set(started),keys)
        for key in keys: self.assertIn(f'["{key}"]',source)
        check={'self_driving','rig_folder','rig_ready','truck_type','drivable_prefab','prefab','wheels','last_ride','problems'}
        self.assertEqual(set(self.mock.call('drive',{'op':'check'})),check)
        for key in check: self.assertIn(f'["{key}"]',source)
        # While it drives: no second drive, no run_until, no teleport or vehicle op on that truck.
        refusal('drive',{'id':T,'place':'bay:hells-kitchen'},f'A drive is under way ({T} to bay:hells-kitchen): wait for it (drive status).',
                'A drive is under way ({drive!.Id} to {drive.Place}): wait for it (drive status).')
        refusal('time',{'run_until':{'time':'09:00'}},f'A drive is under way ({T}), and run_until would end its ride: wait for it (drive status).',
                'A drive is under way ({DrivingVehicle}), and run_until would end its ride: wait for it (drive status).',commands)
        refusal('teleport',{'who':T,'x':1,'z':1},f'{T} is on a drive: wait for it (drive status).','{who} is on a drive: wait for it (drive status).',world)
        refusal('vehicle',{'id':T,'op':'exit'},f'{T} is on a drive: wait for it (drive status).','{id} is on a drive: wait for it (drive status).',world)
        done=self.mock.call('drive',{'op':'status'})
        self.assertEqual((done['running'],done['phase'],done['error'],done['park']['where']),(False,'done',None,'bay:hells-kitchen'))
        self.assertEqual(set(done),keys)
        self.assertIn('walk, drive, vehicle',commands)

    def test_health_has_time_skip(self):
        self.assertFalse(self.mock.health()['time_skip'])
        self.mock.time_skip=True
        self.assertTrue(self.mock.health()['time_skip'])


if __name__=='__main__':unittest.main()
