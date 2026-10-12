from contextlib import redirect_stderr
import io
from pathlib import Path
import sys
import unittest
from unittest.mock import patch
import urllib.error

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from ftlib.bridge import Bridge, BridgeError
from ftlib.mcp import respond
from ftlib.scenario import Runner, validate
from offline_transport import OfflineSession


class OperationTests(unittest.TestCase):
    def setUp(self):
        self.session=OfflineSession().__enter__()
        self.addCleanup(self.session.__exit__,None,None,None)
        self.bridge=self.session.bridge
        self.mock=self.session.mock

    def test_timeout_recovers_original_mutation_once(self):
        self.mock.operation_delays['debug']=1.25
        with patch.object(self.mock,'call',wraps=self.mock.call) as calls:
            reply=self.bridge.call('debug',{'action':'spawn truck'},timeout_s=.1)
            self.assertEqual(len(self.mock.state['vehicles']),1)
            self.assertEqual(sum(c.args[0]=='debug' for c in calls.call_args_list),1)
        status=self.bridge.op(reply['op'])
        self.assertNotIn('op',status)
        self.assertEqual(status['result']['status'],'done')
        self.assertEqual(status['result']['result'],reply['result'])

    def test_pending_call_keeps_health_events_and_status_available(self):
        self.mock.operation_delays['save']=1.3
        reply=self.mock.operation('save',{'name':'FT pending','timeout_s':.1})
        self.assertFalse(reply['ok'])
        identifier=reply['op']
        status=self.bridge.op(identifier)['result']
        self.assertEqual(status['status'],'pending')
        self.assertEqual(status['session'],reply['session'])
        self.assertTrue(self.bridge.health()['ok'])
        cursor=self.bridge.events()['next']
        self.mock.event('during pending call')
        self.assertEqual(self.bridge.events(cursor)['events'][0]['text'],'during pending call')
        recovered=self.bridge.recover(identifier,2)
        self.assertEqual(recovered['session'],reply['session'])
        self.assertEqual(recovered['result']['name'],'FT pending')
        self.assertEqual(set(recovered['result']),{'name','path','bytes'})

    def test_pending_recovery_exposes_id_and_does_not_count_as_refusal(self):
        self.mock.operation_delays['save']=1.3
        with self.assertRaises(BridgeError) as raised:
            self.bridge.call('save',{'name':'FT pending'},timeout_s=.1,recovery_s=0)
        self.assertIsNotNone(raised.exception.op)
        self.assertFalse(raised.exception.remote)
        self.assertIn('do not repeat',str(raised.exception))
        self.bridge.recover(raised.exception.op,2)

    def test_failed_operation_keeps_error_and_id_in_mcp(self):
        with self.assertRaises(BridgeError) as raised: self.bridge.call('wrong')
        error=raised.exception
        self.assertTrue(error.remote)
        self.assertIsInstance(error.op,int)
        status=self.bridge.op(error.op)['result']
        self.assertEqual(status['status'],'failed')
        self.assertEqual(status['error'],str(error))
        with self.assertRaises(BridgeError) as recovered: self.bridge.recover(error.op)
        self.assertEqual(recovered.exception.op,error.op)
        with redirect_stderr(io.StringIO()):
            reply=respond({'jsonrpc':'2.0','id':1,'method':'tools/call','params':{'name':'ft_call','arguments':{'cmd':'wrong'}}},self.bridge)['result']
        self.assertTrue(reply['isError'])
        self.assertIn('op',reply['structuredContent'])
        status=respond({'jsonrpc':'2.0','id':2,'method':'tools/call','params':{'name':'ft_op','arguments':{'id':error.op}}},self.bridge)['result']
        self.assertEqual(status['structuredContent']['result']['status'],'failed')

    def test_transport_timeout_without_id_never_retries(self):
        bridge=Bridge('http://127.0.0.1:1')
        with patch.object(bridge.http,'open',side_effect=urllib.error.URLError('timeout')) as opened:
            with self.assertRaisesRegex(BridgeError,'outcome is unknown') as raised:
                bridge.call('debug',{'action':'spawn truck'})
        self.assertEqual(opened.call_count,1)
        self.assertIsNone(raised.exception.op)

    def test_timeout_recovery_returns_eventual_failure(self):
        replies=[BridgeError('timed out on the main thread; the command goes on',remote=True,reply={'ok':False,'op':42}),
                 {'ok':True,'result':{'session':self.mock.session,'id':42,'cmd':'save','status':'pending'}},
                 {'ok':True,'result':{'session':self.mock.session,'id':42,'cmd':'save','status':'failed','error':'could not write the save'}}]
        with patch.object(self.bridge,'_request',side_effect=replies) as requests:
            with self.assertRaises(BridgeError) as raised: self.bridge.call('save',{'name':'FT broken'})
        self.assertTrue(raised.exception.remote)
        self.assertEqual(raised.exception.op,42)
        self.assertEqual(str(raised.exception),'could not write the save')
        self.assertEqual([c.args[1]['cmd'] for c in requests.call_args_list],['save','op','op'])

    def test_missing_and_evicted_operations(self):
        with self.assertRaisesRegex(BridgeError,'op needs'): self.bridge.call('op')
        first=self.bridge.call('state')['op']
        for _ in range(500): self.mock.operation('money',{'set':1})
        with self.assertRaisesRegex(BridgeError,'keeps the last 500'): self.bridge.op(first)
        self.assertEqual(len(self.mock.operations),500)

    def test_load_status_and_source_guard(self):
        self.assertIsNone(self.bridge.health()['load'])
        self.bridge.call('load',{'name':'FT bridge'})
        self.assertEqual(self.bridge.health()['load']['status'],'pending')
        self.bridge.wait_state('load.status',{'equals':'done'},2)
        self.assertNotIn('error',self.bridge.health()['load'])
        self.assertIsNone(self.bridge.call('state')['result']['load']['error'])
        self.bridge.call('load',{'name':'FT bridge'})
        self.assertEqual(self.bridge.health()['load']['status'],'pending')
        self.bridge.wait_state('load.status',{'equals':'done'},2)
        self.mock.state['save']='Personal'
        with self.assertRaisesRegex(BridgeError,'unsaved changes'): self.bridge.call('load',{'name':'FT bridge'})

    def test_new_state_order_payment_and_exact_boundary(self):
        self.bridge.call('load',{'name':'FT commissary-pay-on-delivery'})
        self.bridge.wait_state('load.status',{'equals':'done'},2)
        v=self.bridge.call('state')['result']['vehicles'][0]
        self.assertTrue(v['legal'])
        state=self.bridge.call('state')['result']
        self.assertEqual(state['player']['activity'],'on_foot')
        self.assertIsNone(state['player']['clock_held_by'])
        self.assertEqual(len(state['commissary']['bays']),5)
        money=state['money']
        order=self.bridge.call('commissary',{'op':'order','site':'hells-kitchen','items':[{'item':'Hotdog','boxes':2}]})['result']['orders'][0]
        self.assertEqual(order['total'],order['goods']+order['fee'])
        self.assertEqual(self.bridge.call('state')['result']['money'],money)
        self.bridge.call('time',{'run_until':{'day':order['due_day'],'time':'07:59'},'speed':60})
        self.bridge.wait_state('clock.paused',{'equals':True},3)
        before=self.bridge.call('state')['result']
        self.assertEqual(before['clock']['arrived_at'],'day 2 07:59')
        self.assertIsNone(before['clock']['run_until'])
        self.assertEqual(before['clock']['speed'],1)
        self.assertEqual(before['transactions_today'],[])
        self.bridge.call('time',{'run_until':{'day':2,'time':'08:00'},'speed':60})
        self.bridge.wait_state('clock.paused',{'equals':True},3)
        paid=self.bridge.call('state')['result']['transactions_today']
        self.assertEqual(len(paid),1)
        self.assertEqual((paid[0]['amount'],paid[0]['day'],paid[0]['time']),(-order['total'],2,'08:00'))
        self.mock.state['clock']['day']=3
        self.assertEqual(len(self.mock.read_state()['transactions_today']),1)
        self.mock.state['clock']['day']=4
        self.assertEqual(self.mock.read_state()['transactions_today'],[])

    def test_crew_assignment_availability_notices_and_errors(self):
        v=self.mock.spawn('truck')
        self.bridge.call('debug',{'action':'hire'})
        employee=self.bridge.call('state')['result']['employees'][0]
        self.assertEqual(set(employee),{'id','name','skill','satisfaction','wage'})
        args={'vehicle':v['id'],'employee_id':employee['id']}
        reply=self.bridge.call('crew',{'op':'assign',**args})['result']
        self.assertTrue(reply['done']);self.assertEqual(len(reply['notices']),1)
        self.assertEqual(self.bridge.call('state')['result']['employees'],[])
        with self.assertRaisesRegex(BridgeError,'already'): self.bridge.call('crew',{'op':'assign',**args})
        self.assertTrue(self.bridge.call('crew',{'op':'unassign',**args})['result']['done'])
        self.assertEqual(self.bridge.call('state')['result']['employees'][0]['id'],employee['id'])
        self.mock.can_write=False
        with self.assertRaisesRegex(BridgeError,'read-only'): self.bridge.call('crew',{'op':'assign',**args})

    def test_panel_refusal_has_done_false(self):
        v=self.mock.spawn('truck')
        self.bridge.call('vehicle',{'id':v['id'],'op':'enter'})
        self.bridge.call('time',{'pause':False})
        reply=self.bridge.call('vehicle',{'id':v['id'],'op':'open'})
        self.assertTrue(reply['ok']);self.assertFalse(reply['result']['done'])

    def test_order_merges_duplicate_items_and_quotes_each_line(self):
        self.mock.restore('FT commissary-pay-on-delivery')
        result=self.bridge.call('commissary',{'op':'order','site':'hells-kitchen','items':[
            {'item':'Hotdog','boxes':1},{'item':'ba:itemname_hotdog','boxes':1},
            {'item':'foodtrucks:itemname_tacos','boxes':2}]})['result']
        self.assertEqual(len(result['orders']),2)
        for order in result['orders']:
            self.assertEqual(order['units'],1000)
            self.assertEqual(order['total'],order['goods']+order['fee'])
        for rows,error in [([{'item':'missing','boxes':1}],"isn't on"),([{'item':'Hotdog','boxes':0}],'no boxes'),([{'item':'Hotdog','boxes':11}],'more than 10')]:
            with self.assertRaisesRegex(BridgeError,error): self.bridge.call('commissary',{'op':'order','site':'hells-kitchen','items':rows})

    def test_bay_readiness_and_refusals(self):
        for status,error in [('pending',"isn't resolved yet"),('failed','has no bay')]:
            self.mock.bay_status['hells-kitchen']=status
            bay=next(b for b in self.mock.bays() if b['site']=='hells-kitchen')
            self.assertEqual(bay['status'],status)
            self.assertIsNone(bay['point'])
            with self.assertRaisesRegex(BridgeError,error): self.bridge.call('teleport',{'who':'player','place':'bay:hells-kitchen'})
            with self.assertRaisesRegex(BridgeError,error): self.bridge.call('shoot',{'name':'pending','pose':'bay-road','target':'bay:hells-kitchen'})

    def test_transaction_filters_and_log_captures(self):
        self.mock.state['transactions_today']=[{'type':'rent','amount':-2,'day':1},{'type':'crew','amount':13,'day':1}]
        runner=Runner(self.bridge)
        runner.variables['day']=1
        runner.event_mark=self.mock.events(0)['next']-1
        self.mock.event('Paid $1,234.56.')
        doc=validate({'name':'fields','checks':'unit','steps':[
            {'expect':'state','path':'transactions_today','where':{'type':'crew','day':'$day'},'length':1},
            {'expect':'state','path':'transactions_today','where':{'type':'crew'},'pick':'[0].amount','equals':13},
            {'remember':'negative','from':'state','path':'money','scale':-1},
            {'wait_event':r'Paid \$(?P<amount>[0-9,]+\.[0-9]+)\.', 'captures':{'amount':'amount'}}]})
        runner.cursor=self.mock.events(0)['next']-1
        for i,step in enumerate(doc['steps'],1): runner.step(step,i,Path(self.session.folder.name))
        self.assertEqual(runner.variables['amount'],1234.56)
        self.assertEqual(runner.variables['negative'],-20000)


if __name__=='__main__': unittest.main()
