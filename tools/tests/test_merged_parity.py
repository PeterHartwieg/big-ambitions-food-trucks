import copy
import os
from pathlib import Path
import sys
import tempfile
import threading
import time
import unittest
from unittest.mock import patch

TOOLS=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(TOOLS))
from ftlib.bridge import BridgeError
from ftlib.scenario import Runner
from offline_transport import OfflineSession


class MergedParityTests(unittest.TestCase):
    def setUp(self):
        self.session=OfflineSession().__enter__()
        self.addCleanup(self.session.__exit__,None,None,None)
        self.mock=self.session.mock
        self.bridge=self.session.bridge

    def test_save_file_shape_and_serialization_compression_failures(self):
        result=self.bridge.call('save',{'name':'FT verified'})['result']
        self.assertEqual(set(result),{'name','path','bytes'})
        path=Path(result['path'])
        self.assertEqual(path.stat().st_size,result['bytes'])
        self.assertGreater(result['bytes'],0)
        snapshot=copy.deepcopy(self.mock.saves['FT verified'])
        contents=path.read_bytes()
        for failure,error in [('serialization',"The game's save failed while writing the city (see Player.log: Save game serialization failed)."),
                              ('compression','The game\'s save failed while compressing (no "Compressed to" line).')]:
            self.mock.save_failure=failure
            self.mock.state['money']=7
            with self.subTest(failure=failure),self.assertRaises(BridgeError) as raised:
                self.bridge.call('save',{'name':'FT verified'})
            self.assertEqual(str(raised.exception),error)
            self.assertTrue(raised.exception.remote)
            status=self.bridge.op(raised.exception.op)['result']
            self.assertEqual((status['status'],status['error']),('failed',error))
            self.assertEqual(path.read_bytes(),contents)
            self.assertEqual(self.mock.saves['FT verified'],snapshot)
        self.mock.save_failure=None
        self.mock.save_in_progress=True
        with self.assertRaisesRegex(BridgeError,'already being written'): self.bridge.call('save',{'name':'FT busy'})
        self.mock.save_in_progress=False
        self.mock.save_failure='refused'
        with self.assertRaisesRegex(BridgeError,'refused to save here'): self.bridge.call('save',{'name':'FT refused'})

    def test_save_checks_missing_stale_and_empty_outputs(self):
        with patch.object(Path,'write_bytes',return_value=0):
            with self.assertRaisesRegex(BridgeError,"save file isn't there"): self.bridge.call('save',{'name':'FT missing'})
        old=self.bridge.call('save',{'name':'FT old'})['result']
        os.utime(old['path'],(time.time()-60,time.time()-60))
        with patch.object(Path,'write_bytes',return_value=1):
            with self.assertRaisesRegex(BridgeError,"wasn't written by this save"): self.bridge.call('save',{'name':'FT old'})
        with patch('zlib.compress',return_value=b''):
            with self.assertRaisesRegex(BridgeError,'save file is empty'): self.bridge.call('save',{'name':'FT empty'})

    def test_vehicle_replies_include_null_or_panel_reason(self):
        v=self.mock.spawn('truck')
        args={'id':v['id']}
        reply=self.bridge.call('vehicle',{**args,'op':'enter'})['result']
        self.assertIsNone(reply['why'])
        for op in ('restock','rent_bay'):
            reply=self.bridge.call('vehicle',{**args,'op':op})['result']
            self.assertFalse(reply['done'])
            self.assertEqual(reply['why'],'the Commissary button refused (see the notice)')
            self.assertEqual(len(reply['notices']),1)
        self.mock.place_vehicle(v,'hells-kitchen')
        reply=self.bridge.call('vehicle',{**args,'op':'rent_bay'})['result']
        self.assertTrue(reply['done']);self.assertIsNone(reply['why'])
        reply=self.bridge.call('vehicle',{**args,'op':'rent_bay'})['result']
        self.assertFalse(reply['done'])
        self.assertEqual(reply['why'],'the Commissary button offers release, nostock, order here, not rent or move')
        reply=self.bridge.call('vehicle',{**args,'op':'restock'})['result']
        self.assertFalse(reply['done']);self.assertIsNone(reply['why'])
        self.bridge.call('time',{'pause':False})
        self.bridge.call('vehicle',{**args,'op':'exit'})
        reply=self.bridge.call('vehicle',{**args,'op':'open'})['result']
        self.assertFalse(reply['done']);self.assertIsNone(reply['why'])

    def test_unload_fails_deferred_and_queued_work_and_resets_events(self):
        self.bridge.call('time',{'run_until':{'time':'08:00'},'speed':1})
        self.mock.load={'name':'FT queued','status':'pending','error':None}
        self.mock.operation_delays['shoot']=20
        responses={}
        def run(name,cmd,args): responses[name]=self.mock.operation(cmd,args)
        shot=threading.Thread(target=run,args=('shot','shoot',{'name':'pending','pose':'door','target':'commissary:hells-kitchen'}))
        shot.start()
        deadline=time.monotonic()+1
        path=self.mock.shots/'pending.png'
        while not path.exists() and time.monotonic()<deadline: time.sleep(.01)
        self.assertTrue(path.exists())
        self.mock.call_gate.acquire()
        try:
            queued=threading.Thread(target=run,args=('queued','debug',{'action':'spawn truck'}))
            queued.start()
            deadline=time.monotonic()+1
            while len(self.mock.operation_waiters)<2 and time.monotonic()<deadline: time.sleep(.01)
            self.assertEqual(len(self.mock.operation_waiters),2)
            self.mock.unload()
            shot.join(1);queued.join(1)
            self.assertFalse(shot.is_alive());self.assertFalse(queued.is_alive())
            for reply in responses.values():
                self.assertFalse(reply['ok']);self.assertEqual(reply['error'],'the bridge stopped')
            self.assertEqual(set(responses),{'shot','queued'})
            self.assertFalse(path.exists())
            self.assertEqual(self.mock.operations,{})
            self.assertEqual(self.mock.last_ui,[])
            self.assertIsNone(self.mock.load)
            self.assertIsNone(self.mock.until)
            self.assertEqual(self.mock.state['clock']['speed'],1)
            self.assertIsNone(self.mock.state['clock']['arrived_at'])
            self.assertEqual(self.mock.events(0),{'ok':True,'session':self.mock.session,'next':1,'dropped':0,'events':[]})
            for cmd in ('state','op','save','ui','quit'):
                reply=self.mock.operation(cmd,{})
                self.assertEqual(reply,{'ok':False,'session':self.mock.session,'error':'the bridge stopped'})
            with self.assertRaisesRegex(BridgeError,'^the bridge stopped$'): self.bridge.call('state')
            self.mock.start_bridge()
        finally: self.mock.call_gate.release()
        deadline=time.monotonic()+1
        while self.mock.operation_waiters and time.monotonic()<deadline: time.sleep(.01)
        self.bridge.reset_session()
        self.assertEqual(self.bridge.call('state')['result']['vehicles'],[])
        self.mock.event('new session')
        self.assertEqual(self.bridge.events(1)['events'][0]['seq'],1)
        self.assertIsNone(self.bridge.health()['load'])

    def test_client_detects_restart_in_event_reads_and_waits(self):
        cursor=self.bridge.events()['next']
        self.mock.unload();self.mock.start_bridge();self.mock.event('new event')
        for read in (lambda:self.bridge.events(cursor),lambda:self.bridge.wait_event('new event',.1,cursor)):
            with self.assertRaises(BridgeError) as raised: read()
            self.assertTrue(raised.exception.restart)
            self.assertIn('Bridge restarted',str(raised.exception))
        self.bridge.reset_session()
        self.assertEqual(self.bridge.events(1)['events'][0]['seq'],1)
        self.assertEqual(self.bridge.wait_event('new event',.1,1)['seq'],1)
        self.mock.unload()
        with self.assertRaises(BridgeError) as raised: self.bridge.recover(123)
        self.assertEqual(str(raised.exception),'the bridge stopped')
        self.assertTrue(raised.exception.remote)
        self.assertEqual(raised.exception.op,123)

    def test_restart_fails_even_a_soft_scenario_step(self):
        old=self.mock.call
        def restart(cmd,args):
            if cmd=='console':
                self.mock.unload();self.mock.start_bridge()
                return {'output':['restarted']}
            return old(cmd,args)
        with patch.object(self.mock,'call',side_effect=restart),tempfile.TemporaryDirectory() as folder:
            report,_=Runner(self.bridge).run({'name':'restart','checks':'unit','steps':[
                {'call':'console','args':{'line':'restart'},'soft':True},{'note':'end'}]},folder,keep_going=True)
        self.assertFalse(report['ok'])
        self.assertTrue(any('Bridge restarted' in s for s in report['summary']))
        self.assertEqual(report['events'],0)

    def test_teleport_cancels_entry_and_hatch_return_walks(self):
        v=self.mock.spawn('truck')
        for op in ('enter','back_to_hatch'):
            self.mock.state['player'].update(x=100,in_vehicle=None,at_hatch=None)
            v['open']=op=='back_to_hatch'
            self.bridge.call('vehicle',{'id':v['id'],'op':op})
            self.assertTrue(self.mock.walks)
            self.bridge.call('teleport',{'who':'player','x':500,'z':500})
            self.assertEqual(self.mock.walks,[])
            self.bridge.call('time',{'pause':False})
            time.sleep(.1)
            self.assertIsNone(self.mock.state['player']['in_vehicle'])
            self.assertIsNone(self.mock.state['player']['at_hatch'])
            self.assertEqual(self.mock.read_state()['player']['activity'],'on_foot')
            self.bridge.call('time',{'pause':True})

    def test_character_recovery_exemption_and_first_name_order(self):
        self.mock.store('Recover #0');self.mock.store('Recover #12');self.mock.store('Recover Midnight')
        self.bridge.call('money',{'set':123})
        self.mock.store('Z city');self.mock.store('A city')
        with self.assertRaises(BridgeError) as raised: self.bridge.call('money',{'set':456})
        self.assertEqual(str(raised.exception),"This save's character also has saves not starting with FT (A city); make FT saves with a character of their own.")
        self.assertEqual(self.mock.state['money'],123)
        del self.mock.saves['A city'];del self.mock.saves['Z city']
        self.mock.store('recover #0')
        with self.assertRaisesRegex(BridgeError,'recover #0'): self.bridge.call('money',{'set':456})

    def test_ui_confirmations_native_owners_and_missing_option_label(self):
        refusal="ui click refuses the game's save and load controls; use the save and load commands."
        self.mock.pause_menu_open=True;self.mock.confirmation_open=True
        entries=[{'id':1,'text':'Yes','kind':'button','path':'Canvas/HudConfirmUi/Yes'}]
        with patch.object(self.mock,'buttons',return_value=entries):
            with self.assertRaises(BridgeError) as raised: self.bridge.call('ui',{'op':'click','text':'Yes'})
            self.assertEqual(str(raised.exception),refusal)
        self.assertEqual(self.bridge.call('ui',{'op':'escape'})['result'],{'closed':'confirmation'})
        self.assertTrue(self.mock.pause_menu_open)
        self.assertEqual(self.bridge.call('ui',{'op':'escape'})['result'],{'closed':'pause menu'})
        for path in ('FuneralUI/NewCharacter','LoadGameCharacterEntryView/Remove'):
            entries=[{'id':1,'text':'Go','kind':'button','path':path}]
            with patch.object(self.mock,'buttons',return_value=entries):
                with self.assertRaisesRegex(BridgeError,'save and load controls'): self.bridge.call('ui',{'op':'click','text':'Go'})
        entries=[{'id':1,'text':'Save','kind':'button','path':'ModPanel/CustomAction'}]
        with patch.object(self.mock,'buttons',return_value=entries):
            self.assertEqual(self.bridge.call('ui',{'op':'click','text':'Save'})['result'],{'clicked':'Save'})
        original=[{'id':1,'text':'One','kind':'option','path':'Dropdown/option[0]'},
                  {'id':2,'text':'Two','kind':'option','path':'Dropdown/option[1]'}]
        with patch.object(self.mock,'buttons',return_value=original): self.bridge.call('ui',{'op':'list'})
        with patch.object(self.mock,'buttons',return_value=original[:1]):
            with self.assertRaises(BridgeError) as raised: self.bridge.call('ui',{'op':'click','id':2})
            self.assertEqual(str(raised.exception),"the control with id 2 now reads ''; list again.")
        self.mock.state['scene']='menu'
        self.mock.character_creator=True
        self.bridge.call('ui',{'op':'list'})
        for op in ('click','escape'):
            with self.assertRaisesRegex(BridgeError,'ui works only in a loaded FT city'): self.bridge.call('ui',{'op':op})
        with self.assertRaisesRegex(BridgeError,'not the character creator'): self.bridge.call('load',{'name':'FT mock'})

    def test_health_and_bay_nulls_and_clock_holder_names(self):
        self.bridge.call('load',{'name':'FT bridge'})
        self.assertEqual(set(self.bridge.health()['load']),{'name','status'})
        self.bridge.wait_state('load.status',{'equals':'done'},1)
        self.assertEqual(set(self.bridge.call('state')['result']['load']),{'name','status','error'})
        for status in ('pending','failed'):
            self.mock.bay_status['hells-kitchen']=status
            bay=next(b for b in self.mock.bays() if b['site']=='hells-kitchen')
            self.assertIsNone(bay['point']);self.assertIsNone(bay['entrance'])
        self.mock.dialog='commissary'
        self.assertEqual(self.mock.read_state()['player']['clock_held_by'],'a screen')
        self.mock.dialog='crew'
        self.assertEqual(self.mock.read_state()['player']['clock_held_by'],'phone')
        self.mock.dialog='closed';self.mock.mod_loaded=False
        with self.assertRaisesRegex(BridgeError,'Nothing is open'): self.bridge.call('ui',{'op':'escape'})


if __name__=='__main__': unittest.main()
