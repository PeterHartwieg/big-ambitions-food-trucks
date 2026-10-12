from pathlib import Path
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

TOOLS=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(TOOLS))
from ftlib.bridge import Bridge, BridgeError
from ftlib.logcheck import always_on, check_log, read_rules
from ftlib.mcp import call_tool
from ftlib.scenario import Runner, validate
from offline_transport import OfflineSession


class ReviewParityTests(unittest.TestCase):
    def setUp(self):
        self.session=OfflineSession().__enter__()
        self.addCleanup(self.session.__exit__,None,None,None)
        self.mock=self.session.mock
        self.bridge=self.session.bridge

    def test_ui_city_mod_and_character_guards(self):
        with self.assertRaises(BridgeError) as raised: self.bridge.call('load',{'name':'FT shared character'})
        self.assertEqual(str(raised.exception),"This save's character also has saves not starting with FT (Non-FT mock); make FT saves with a character of their own.")
        self.assertEqual(self.mock.state['save'],'FT mock')
        error='ui works only in a loaded FT city; use load from the main menu.'
        for scene,save,loaded in [('menu',None,False),('loading','FT mock',False),('city','Other',True)]:
            self.mock.state.update(scene=scene,save=save)
            self.mock.mod_loaded=loaded
            for op in ('click','escape'):
                with self.subTest(scene=scene,op=op),self.assertRaises(BridgeError) as raised:
                    self.bridge.call('ui',{'op':op,'text':'Continue'})
                self.assertEqual(str(raised.exception),error)
            self.assertIn('buttons',self.bridge.call('ui',{'op':'list'})['result'])
        self.mock.restore('FT mock');self.mock.mod_loaded=True
        self.mock.store('Other city')
        error="This save's character also has saves not starting with FT (Other city); make FT saves with a character of their own."
        for cmd,args in [('money',{'set':1}),('debug',{'action':'spawn truck'}),('time',{'pause':False}),('save',{'name':'FT copy'}),('ui',{'op':'click','text':'OPEN HERE'}),('ui',{'op':'escape'}),('load',{'name':'FT bridge'})]:
            with self.subTest(cmd=cmd),self.assertRaises(BridgeError) as raised: self.bridge.call(cmd,args)
            self.assertEqual(str(raised.exception),error)
        self.mock.state['scene']='menu'
        with self.assertRaises(BridgeError) as raised: self.bridge.call('load',{'name':'FT mock'})
        self.assertEqual(str(raised.exception),error)
        self.assertEqual(self.mock.state['money'],20000)
        self.assertEqual(self.mock.state['vehicles'],[])
        self.mock.character='different character'
        self.mock.state.update(scene='city',save='FT safe')
        self.mock.store('FT safe')
        self.mock.state['scene']='menu'
        self.bridge.call('load',{'name':'FT safe'})
        self.bridge.wait_state('load.status',{'equals':'done'},1)
        self.bridge.call('debug',{'action':'spawn truck'})
        self.assertEqual(len(self.mock.state['vehicles']),1)

    def test_native_save_controls_and_stale_labels_refuse(self):
        error="ui click refuses the game's save and load controls; use the save and load commands."
        for label,path in [('Save','MiniMenu/Save'),('Load','LoadGame/Load'),('Delete','LoadGameSaveEntryView/Delete'),('Confirm','SaveGameCompatibilityUI/Confirm')]:
            entries=[{'id':1,'text':label,'kind':'button','path':path}]
            with patch.object(self.mock,'buttons',return_value=entries):
                self.bridge.call('ui',{'op':'list'})
                for selector in ({'id':1},{'text':label}):
                    with self.subTest(label=label,selector=selector),self.assertRaises(BridgeError) as raised:
                        self.bridge.call('ui',{'op':'click',**selector})
                    self.assertEqual(str(raised.exception),error)
        for kind in ('button','option'):
            entry={'id':7,'text':'Old row','kind':kind,'path':'Panel/ReusableCell'}
            with patch.object(self.mock,'buttons',return_value=[entry]): self.bridge.call('ui',{'op':'list'})
            updated={**entry,'text':'New row'}
            with patch.object(self.mock,'buttons',return_value=[updated]):
                with self.assertRaises(BridgeError) as raised: self.bridge.call('ui',{'op':'click','id':7})
            self.assertEqual(str(raised.exception),"the control with id 7 now reads 'New row'; list again.")

    def test_walking_stays_paused_but_load_finishes(self):
        v=self.mock.spawn('truck')
        self.mock.state['player']['x']=100
        self.bridge.call('vehicle',{'id':v['id'],'op':'enter'})
        time.sleep(.12)
        self.assertIsNone(self.mock.state['player']['in_vehicle'])
        self.assertEqual(self.mock.read_state()['player']['activity'],'walking')
        self.bridge.call('time',{'pause':False})
        self.bridge.wait_state('player.in_vehicle',{'equals':v['id']},1)
        self.bridge.call('vehicle',{'id':v['id'],'op':'exit'})
        self.bridge.call('time',{'pause':True})
        v['open']=True
        self.bridge.call('vehicle',{'id':v['id'],'op':'back_to_hatch'})
        time.sleep(.12)
        self.assertIsNone(self.mock.state['player']['at_hatch'])
        self.assertEqual(self.mock.read_state()['player']['activity'],'walking')
        self.bridge.call('time',{'pause':False})
        self.bridge.wait_state('player.at_hatch',{'equals':v['id']},1)
        self.bridge.call('vehicle',{'id':v['id'],'op':'close'})
        self.bridge.call('time',{'pause':True})
        self.bridge.call('load',{'name':'FT bridge'})
        self.bridge.wait_state('load.status',{'equals':'done'},1)
        self.assertTrue(self.mock.state['clock']['paused'])

    def test_commissary_panel_refusals_keep_ok_done_and_notices(self):
        v=self.mock.spawn('truck')
        self.bridge.call('vehicle',{'id':v['id'],'op':'enter'})
        for op in ('restock','rent_bay'):
            reply=self.bridge.call('vehicle',{'id':v['id'],'op':op})
            self.assertTrue(reply['ok']);self.assertFalse(reply['result']['done'])
            self.assertEqual(reply['result']['notices'],["Park the truck in a commissary's loading bay first."])
        self.mock.place_vehicle(v,'hells-kitchen')
        self.assertTrue(self.bridge.call('vehicle',{'id':v['id'],'op':'rent_bay'})['result']['done'])
        reply=self.bridge.call('vehicle',{'id':v['id'],'op':'rent_bay'})
        self.assertTrue(reply['ok']);self.assertFalse(reply['result']['done'])
        reply=self.bridge.call('vehicle',{'id':v['id'],'op':'restock'})
        self.assertTrue(reply['ok']);self.assertFalse(reply['result']['done'])
        self.assertEqual(len(reply['result']['notices']),1)
        self.mock.can_write=False
        reply=self.bridge.call('vehicle',{'id':v['id'],'op':'rent_bay'})
        self.assertTrue(reply['ok']);self.assertFalse(reply['result']['done'])
        self.assertEqual(len(reply['result']['notices']),1)

    def test_timeout_clamps_body_and_transport_deadline(self):
        bridge=Bridge('http://127.0.0.1:1')
        for requested,expected in [(-100,1),(0,1),(.1,1),(1,1),(50,50),(600,600),(10**1000,600)]:
            with self.subTest(requested=requested),patch.object(bridge,'_request',return_value={'ok':True,'op':1,'result':{}}) as request:
                bridge.call('state',timeout_s=requested)
                self.assertEqual(request.call_args.args[1]['args']['timeout_s'],expected)
                self.assertEqual(request.call_args.args[2],expected+2)
            with patch.object(bridge,'_request',return_value={'ok':True,'op':1,'result':{}}) as request:
                self.assertFalse(call_tool('ft_call',{'cmd':'state','timeout_s':requested},bridge)['isError'])
                self.assertEqual(request.call_args.args[1]['args']['timeout_s'],expected)
        with patch.object(bridge,'_request',return_value={'ok':True,'result':{}}) as request:
            bridge.call('state',{'timeout_s':9999},timeout_s=2)
            self.assertEqual(request.call_args.args[1]['args']['timeout_s'],600)
        for bad in (True,None,'long',float('nan'),float('inf')):
            with self.subTest(bad=bad),self.assertRaises(BridgeError): bridge.call('state',{'timeout_s':bad})
        class Finished:
            def wait(self, seconds):
                waited.append(seconds)
                return True
            def set(self): pass
        class InlineThread:
            def __init__(self,target,**kwargs): self.target=target
            def start(self): self.target()
        for requested,expected in [(-100,1),(.1,1),(10**100,600)]:
            waited=[]
            with patch('threading.Event',Finished),patch('threading.Thread',InlineThread): self.mock.operation('state',{'timeout_s':requested})
            self.assertEqual(waited,[expected])


class EventBoundaryTests(unittest.TestCase):
    class Scripted:
        def __init__(self): self.log=[]
        def health(self): return {'ok':True,'scene':'city','save':'FT isolated'}
        def events(self,since=0): return {'ok':True,'next':len(self.log)+1,'dropped':0,'events':[e for e in self.log if e['seq']>=since]}
        def call(self,cmd,args=None,timeout_s=10):
            if cmd=='initial_sales':
                self.log += [{'seq':1,'text':'Sale: first visit'},{'seq':2,'text':'Sale: first visit again'}]
            elif cmd=='fresh_sale': self.log.append({'seq':len(self.log)+1,'text':'Sale: return visit'})
            return {'ok':True,'op':1,'result':{'serving':'player'}}

    def run_steps(self,steps):
        with tempfile.TemporaryDirectory() as folder:
            return Runner(self.Scripted()).run({'name':'sales-window','checks':'unit','steps':steps},folder)[0]

    def test_two_early_sales_do_not_pass_return_check(self):
        for explicit in (False,True):
            wait={'wait_event':'Sale:','timeout_s':.01}
            if explicit: wait['after_step']=3
            steps=[{'call':'initial_sales'},{'wait_event':'Sale:','timeout_s':.01},
                   {'call':'step_out'},{'call':'back_to_hatch'},
                   {'expect':'state','path':'serving','equals':'player'},wait]
            with self.subTest(explicit=explicit):
                report=self.run_steps(steps)
                self.assertFalse(report['ok'])
                self.assertEqual(report['steps'][-1]['kind'],'wait_event')
                self.assertIn('no match',report['steps'][-1]['seen'])

    def test_previous_step_event_and_explicit_action_window(self):
        report=self.run_steps([{'call':'initial_sales'},{'wait_event':'Sale:'}])
        self.assertTrue(report['ok'],report['summary'])
        report=self.run_steps([{'call':'initial_sales'},{'wait_event':'Sale:'},{'call':'fresh_sale'},
                               {'expect':'state','path':'serving','equals':'player'},
                               {'wait_event':'return visit','after_step':2}])
        self.assertTrue(report['ok'],report['summary'])
        for after in (-1,True,1,1.5):
            with self.subTest(after=after),self.assertRaises(ValueError):
                validate({'name':'bad','checks':'unit','steps':[{'wait_event':'x','after_step':after}]})


class MissingStateTests(unittest.TestCase):
    def test_client_waits_for_missing_key_filter_and_nested_value(self):
        bridge=Bridge('http://127.0.0.1:1')
        for state,path in [({},'ready'),({'rows':[]},'rows[id=truck].ready'),({'player':None},'player.ready')]:
            ready={'ready':True,'rows':[{'id':'truck','ready':True}],'player':{'ready':True}}
            with self.subTest(path=path),patch.object(bridge,'call',side_effect=[{'result':state},{'result':ready}]) as calls:
                self.assertTrue(bridge.wait_state(path,{'not_equals':None},.05,.001))
                self.assertEqual(calls.call_count,2)
        with patch.object(bridge,'call',return_value={'result':{}}) as calls:
            with self.assertRaisesRegex(BridgeError,'No value at'): bridge.wait_state('missing',{'not_equals':None},.01,.001)
            self.assertGreater(calls.call_count,1)

    def test_runner_waits_and_times_out_but_expect_fails_immediately(self):
        class Changing(EventBoundaryTests.Scripted):
            def __init__(self): super().__init__();self.reads=0
            def call(self,*args):
                self.reads+=1
                return {'ok':True,'op':self.reads,'result':{} if self.reads==1 else {'rows':[{'id':'truck','ready':True}]}}
        for kind in ('wait_state','expect'):
            bridge=Changing()
            step={kind:'rows[id=truck].ready','not_equals':None,'timeout_s':.02,'poll_s':.001} if kind=='wait_state' else {'expect':'state','path':'rows[id=truck].ready','not_equals':None}
            with tempfile.TemporaryDirectory() as folder: report,_=Runner(bridge).run({'name':'missing','checks':'unit','steps':[step]},folder)
            self.assertEqual(report['ok'],kind=='wait_state')
            self.assertEqual(bridge.reads,2 if kind=='wait_state' else 1)
        bridge=EventBoundaryTests.Scripted()
        with tempfile.TemporaryDirectory() as folder:
            report,_=Runner(bridge).run({'name':'missing','checks':'unit','steps':[{'wait_state':'missing','not_equals':None,'timeout_s':.01,'poll_s':.001}]},folder)
        self.assertFalse(report['ok']);self.assertIn('No value at',report['summary'][0])


class ReviewLogTests(unittest.TestCase):
    def test_real_format_error_without_error_word_in_payload(self):
        line="[Mod:FoodTrucks] ERROR: Cart truck-1: 2 held sales dropped, the cart or the data is gone."
        self.assertNotIn('error',line.split('ERROR: ')[1].lower())
        self.assertEqual(always_on(line)['mod_errors'],[line])
        old='[Mod:FoodTrucks] Error: Hires failed'
        self.assertEqual(always_on(old)['mod_errors'],[old])
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'Player.log';path.write_text(line+'\n')
            self.assertFalse(check_log(path,'unknown')['ok'])

    def test_rules_match_only_their_full_log_lines(self):
        rules={r.id:r for r in read_rules(TOOLS.parent/'tests/game/logchecks/0.9.10.txt')}
        good='[Mod:FoodTrucks] Map pick: no truck spot by First Street 22 (entrance x 10.0, z 20.0): no legal place with the hatch to the pavement within 40 m (2 parking colliders, 4 lane segments near) (10 ms).'
        self.assertEqual(rules['map.noSpot'].check(good)['status'],'PASS')
        for bad in [good.replace('no legal place with the hatch to the pavement within 40 m','neither parking lanes nor traffic lanes within reach'), 'neither parking lanes nor traffic lanes within reach','unrelated '+good,good.replace('truck','cart')]:
            self.assertEqual(rules['map.noSpot'].check(bad)['status'],'FAIL')
        template="[Mod:FoodTrucks] BizMan: vehicle page built from the warehouse driver row (Drivers/Row) and the logistics plan (Plans; stop rows dragged by Handle; the copied list's verticalLayoutGroup {vertical}, contentSizeFitter {fitter}); it sits in Panel (laid out by its parent, scrolling)."
        for vertical in ("at ''",'none'):
            for fitter in ("at ''",'none'):
                line=template.format(vertical=vertical,fitter=fitter)
                self.assertEqual(rules['bizman.layout'].check(line)['status'],'PASS')
                self.assertEqual(rules['bizman.build'].check(line)['status'],'PASS')
        for bad in ["the copied list's unrelated none",template.format(vertical='none',fitter='none').replace('verticalLayoutGroup','other'),template.format(vertical="at 'elsewhere'",fitter='none')]:
            self.assertEqual(rules['bizman.layout'].check(bad)['status'],'FAIL')
        for how in ('by a parking lane, pavement on the right 2 m out, cart 1 m out','by the nearest driving lane (no parking lane fits)'):
            line=f'[Mod:FoodTrucks] Map pick: cart stop by First Street 22 at x 10.0, y 0.0, z 20.0, heading 0, 3 m from the entrance; {how}; 2 lane segments (10 ms).'
            self.assertEqual(rules['map.cartSpot'].check(line)['status'],'PASS')
        self.assertEqual(rules['map.cartSpot'].check('by the nearest driving lane (no parking lane fits)')['status'],'FAIL')


if __name__=='__main__': unittest.main()
