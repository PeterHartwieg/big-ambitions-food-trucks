import base64
from collections import Counter
import json
import os
from pathlib import Path
import selectors
import struct
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import patch
import zlib

TOOLS = Path(__file__).resolve().parents[1]
ROOT = TOOLS.parent
sys.path.insert(0, str(TOOLS))
from ftlib.bridge import Bridge, BridgeError
from ftlib.logcheck import Rule, always_on, check_log, read_rules
from ftlib.paths import lookup, meets, parse_args, PathError
from ftlib.scenario import Runner, read_scenario, validate
from ftlib.shots import contact_sheet, fetch_shot


def line_from_pipe(process):
    with selectors.DefaultSelector() as selector:
        selector.register(process.stdout, selectors.EVENT_READ)
        if not selector.select(10):
            raise AssertionError('Process did not return a line within 10 seconds')
    line = process.stdout.readline()
    if not line:
        raise AssertionError('Process exited without returning a line: ' + process.stderr.read().strip())
    return line


class MockSession:
    def __enter__(self):
        self.process = subprocess.Popen([str(TOOLS / 'mock-bridge'), '--port', '0', '--clock-rate', '100'],
                                        stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        try:
            self.url = line_from_pipe(self.process).strip().split()[-1]
            assert self.url.startswith('http://127.0.0.1:')
            self.bridge = Bridge(self.url)
            return self
        except BaseException:
            self.__exit__(None, None, None)
            raise

    def __exit__(self, *args):
        self.process.terminate()
        self.process.communicate(timeout=10)


OFFLINE = os.environ.get('FT_TEST_TRANSPORT') == 'inprocess'
if OFFLINE:
    from offline_transport import OfflineSession, BOOT
    MockSession = OfflineSession


def entry_command(entry, *args):
    if OFFLINE:
        return [sys.executable, '-c', BOOT, str(TOOLS), str(TOOLS / entry), *args]
    return [str(TOOLS / entry), *args]


class PathsTests(unittest.TestCase):
    def setUp(self):
        self.state = {'vehicles': [{'kind':'cart','legal':False,'stock':[]},
                                   {'kind':'truck','legal':True,'stock':[{'amount':62}]}], 'null':None}

    def test_paths_indexes_and_first_filter(self):
        self.assertTrue(lookup(self.state,'vehicles[kind=truck].legal'))
        self.assertEqual(lookup(self.state,'vehicles.1.stock.0.amount'),62)
        self.assertEqual(lookup(self.state,'vehicles[1].stock[0].amount'),62)
        self.assertFalse(lookup(self.state,'vehicles[legal=false].legal'))
        self.assertEqual(lookup({'rows':[None, {}, {'id':'found'}]},'rows[id=found].id'),'found')
        self.assertIsNone(lookup(self.state,'null'))
        self.assertEqual(lookup(self.state,''),self.state)
        with self.assertRaises(PathError): lookup(self.state,'vehicles[kind=missing].legal')
        for path in ('vehicles..0','vehicles[2].legal','null.field','vehicles[-1','vehicles.1.stock.9.amount'):
            with self.subTest(path=path), self.assertRaises(PathError): lookup(self.state,path)

    def test_call_values(self):
        result=parse_args(['set=50000','pause=true','target=truck=a','run_until.day=4','run_until.time=08:05',
                           'list=[1,2]','nothing=null','label="hello"','ratio=1.25'], '{"speed":20}')
        self.assertEqual(result,{'set':50000,'pause':True,'target':'truck=a','run_until':{'day':4,'time':'08:05'},
                                 'list':[1,2],'nothing':None,'label':'hello','ratio':1.25,'speed':20})
        for pairs,raw in [(['broken'],None),(['x=1','x.y=2'],None),([], '[]'),(['.bad=1'],None)]:
            with self.subTest(pairs=pairs),self.assertRaises(ValueError):parse_args(pairs,raw)

    def test_conditions(self):
        for actual, cond in [(2,{'equals':2,'not_equals':3,'less':4,'greater':1}),
                             ([1,2],{'length':2,'contains':1}),('hello',{'matches':'ell'})]:
            self.assertTrue(meets(actual,cond))
        self.assertFalse(meets(None,{'less':2}))
        self.assertFalse(meets([1],{'length':2}))


class LogTests(unittest.TestCase):
    def test_all_rule_kinds(self):
        text='first\nsecond\nsecond\nlast'
        cases=[('must','second',True),('must','missing',False),('never','missing',True),('never','second',False),
               ('count=2','second',True),('count=1','second',False),('count>=2','second',True),('count>=3','second',False),
               ('order','first >> second >> last',True),('order','last >> first',False)]
        for kind,pattern,expected in cases:
            with self.subTest(kind=kind,pattern=pattern):
                self.assertEqual(Rule('test',kind,pattern).check(text)['status']=='PASS',expected)
        with self.assertRaises(ValueError):Rule('bad','often','x')

    def test_rows_and_unknown_version(self):
        with tempfile.TemporaryDirectory() as folder:
            folder=Path(folder)
            (folder/'1.2.3.txt').write_text('startup | must | ready\nplayed | must | delivery | row 0.9.10/2\n')
            log=folder/'Player.log'
            log.write_text('[Mod:FoodTrucks] Version 1.2.3 loading.\nready\n')
            report=check_log(log,rules_dir=folder)
            self.assertTrue(report['ok'])
            self.assertEqual(report['rules'][1]['status'],'NOT CHECKED')
            self.assertFalse(check_log(log,rows='0.9.10/2',rules_dir=folder)['ok'])
            self.assertFalse(check_log(log,rows='all',rules_dir=folder)['ok'])
            self.assertFalse(check_log(log,'0.9.8',rules_dir=folder)['rules_available'])

    def test_always_on_exception_blocks(self):
        text='''[Mod:FoodTrucks] Patch Good: Active.
[Mod:FoodTrucks] Patch Bad: Not active, method not found.
[Mod:FoodTrucks] ERROR: Hires failed
NullReferenceException: test
  at ThirdPersonCharacter.MoveNext()
FoodTrucks.QueuePerson:Leave()

NullReferenceException: unrelated
 at Other.Code()

'''
        result=always_on(text)
        self.assertEqual([len(result[k]) for k in result],[1,1,1,1])
        self.assertIn('FoodTrucks.QueuePerson',result['exceptions'][0])

    def test_rules_compile_and_patch_names_match_source(self):
        rules=read_rules(ROOT/'tests/game/logchecks/0.9.10.txt')
        self.assertEqual(sum(r.id.startswith('patch.') for r in rules),54)
        self.assertGreater(len(rules),90)
        self.assertTrue(any('BusinessHelper.RunDaily' in r.id for r in rules))


class BridgeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.session=MockSession().__enter__()
        cls.bridge=cls.session.bridge

    @classmethod
    def tearDownClass(cls):
        cls.session.__exit__(None,None,None)

    def setUp(self):
        self.bridge.call('load',{'name':'FT bridge'})
        self.bridge.wait_state('scene',{'equals':'city'},2)

    def test_clock_waits_and_event_cursor(self):
        self.assertEqual(self.bridge.health()['scene'],'city')
        cursor=self.bridge.events()['next']
        self.bridge.call('debug',{'action':'spawn truck'})
        event=self.bridge.wait_event('Spawned',1,cursor)
        self.assertEqual(event['seq'],cursor)
        self.assertTrue(self.bridge.events(event['seq']+1)['events'][0]['kind']=='notice')
        self.bridge.call('time',{'speed':50})
        self.bridge.wait_state('clock.time',{'not_equals':'07:00'},1,.02)
        self.bridge.call('time',{'pause':True})
        with self.assertRaises(BridgeError):self.bridge.wait_event('impossible',.01,self.bridge.events()['next'])
        with self.assertRaises(BridgeError):self.bridge.wait_state('money',{'less':0},.01,.005)

    def test_errors_and_all_contract_commands(self):
        with self.assertRaisesRegex(BridgeError,'Unknown command'):self.bridge.call('unknown')
        for cmd in ('load','new_game','save'):
            with self.assertRaisesRegex(BridgeError,'FT'):self.bridge.call(cmd,{'name':'Personal'})
        self.bridge.call('save',{'name':'FT save'})
        self.assertEqual(self.bridge.call('load',{'name':'FT save'})['result'],{'loading':True,'name':'FT save','character':'Mock character'})
        self.bridge.wait_state('scene',{'equals':'city'},2)
        self.bridge.call('money', {'set':1})
        self.bridge.call('load', {'name':'FT save'})
        self.bridge.wait_state('scene',{'equals':'city'},2)
        self.assertEqual(self.bridge.call('state')['result']['money'],20000)
        self.assertIn('output',self.bridge.call('console',{'line':'IRSForcePayment 1'})['result'])
        with MockSession() as quitting:
            self.assertEqual(quitting.bridge.call('quit')['result'],{})

    def test_ring_buffer_drops(self):
        initial=self.bridge.events()['next']
        for _ in range(2005):self.bridge.call('console',{'line':'mock event'})
        batch=self.bridge.events(initial)
        self.assertEqual(len(batch['events']),2000)
        self.assertGreaterEqual(batch['dropped'],5)
        self.assertEqual(batch['events'][-1]['seq']+1,batch['next'])
        with self.assertRaisesRegex(BridgeError,'Lost'):self.bridge.wait_event('not there',0,initial)

    def test_mock_save_guard_directly(self):
        import runpy
        namespace=runpy.run_path(str(TOOLS/'mock-bridge'),run_name='mock_test')
        with tempfile.TemporaryDirectory() as folder:
            mock=namespace['Mock'](folder)
            mock.state['save']='Personal'
            self.assertIn('money',mock.call('state',{}))
            for cmd,args in [('money',{'set':0}),('debug',{'action':'spawn truck'}),('time',{'pause':True}),('ui',{'op':'click','id':1})]:
                with self.subTest(cmd=cmd),self.assertRaisesRegex(ValueError,'FT city' if cmd=='ui' else 'FT save'):mock.call(cmd,args)

    def test_screenshot_png(self):
        shot=self.bridge.call('shoot',{'name':'png-test','pose':'door','target':'commissary:hells-kitchen','width':17,'height':16})['result']['path']
        data=Path(shot).read_bytes()
        self.assertEqual(data[:8],b'\x89PNG\r\n\x1a\n')
        self.assertEqual(struct.unpack('!II',data[16:24]),(17,16))
        pos=8
        compressed=b''
        while pos<len(data):
            size=struct.unpack('!I',data[pos:pos+4])[0]
            kind=data[pos+4:pos+8]; payload=data[pos+8:pos+8+size]
            self.assertEqual(struct.unpack('!I',data[pos+8+size:pos+12+size])[0],zlib.crc32(kind+payload)&0xffffffff)
            if kind==b'IDAT':compressed+=payload
            pos+=12+size
        self.assertEqual(len(zlib.decompress(compressed)),16*(1+17*3))


class ScenarioTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.session=MockSession().__enter__()

    @classmethod
    def tearDownClass(cls):cls.session.__exit__(None,None,None)

    def setUp(self):self.session.bridge.call('load',{'name':'FT bridge'}); self.session.bridge.wait_state('scene',{'equals':'city'},2)

    def test_every_scenario_end_to_end(self):
        for path in sorted((ROOT/'tests/game/scenarios').glob('*.json')):
            with self.subTest(scenario=path.name),tempfile.TemporaryDirectory() as folder:
                self.session.bridge.call('load',{'name':'FT bridge'}); self.session.bridge.wait_state('scene',{'equals':'city'},2)
                runner=Runner(self.session.bridge)
                report,directory=runner.run(read_scenario(path),folder)
                self.assertTrue(report['ok'],'; '.join(report['summary']))
                self.assertFalse(any(s['status']=='FAIL' for s in report['steps']),report['summary'])
                self.assertEqual(len(report['steps']),len(read_scenario(path)['steps']))
                self.assertTrue(all((directory/name).is_file() for name in ('report.md','report.json','events.jsonl','index.html')))
                sequences=[e['seq'] for e in runner.events]
                self.assertEqual(sequences,sorted(set(sequences)))
                if path.stem=='bay-shots':self.assertEqual(len(list(directory.glob('*.png'))),30)
                if path.stem=='crew-midnight-payout':
                    paid=[e for e in runner.events if e['text'].startswith('Crew sales paid in:')]
                    self.assertEqual(len(paid),1)
                    self.assertIn('day 1:',paid[0]['text'])
                    last_hour=next(e for e in runner.events if 'day 1 23:00 hour:' in e['text'])
                    catch=next(e for e in runner.events if 'caught up to minute' in e['text'])
                    self.assertLess(last_hour['seq'],catch['seq'])
                    self.assertLess(catch['seq'],paid[0]['seq'])

    def test_needs_fail_report_and_soft_keep_going(self):
        base={'name':'test','checks':'unit test','needs':{'scene':'menu'},'steps':[{'note':'never'}]}
        with tempfile.TemporaryDirectory() as folder:
            report,_=Runner(self.session.bridge).run(base,folder)
            self.assertFalse(report['ok'])
            self.assertEqual(report['steps'][0]['kind'],'needs')
        base['needs']={'save_prefix':'FT'}
        base['steps']=[{'expect':'state','path':'money','equals':-1,'soft':True},{'note':'continued'},
                       {'call':'unknown','expect_ok':False},{'sleep_s':.01}]
        with tempfile.TemporaryDirectory() as folder:
            report,_=Runner(self.session.bridge).run(base,folder)
            self.assertTrue(report['ok'])
            self.assertEqual(len(report['steps']),4)
        base['steps'][0]['soft']=False
        for keep,expected in [(False,1),(True,4)]:
            with tempfile.TemporaryDirectory() as folder:
                report,_=Runner(self.session.bridge).run(base,folder,keep)
                self.assertFalse(report['ok'])
                self.assertEqual(len(report['steps']),expected)

    def test_no_event_interval_detects_failure(self):
        document={'name':'interval','checks':'unit','steps':[{'call':'debug','args':{'action':'spawn truck'}},
                    {'no_event':'Spawned','from_step':0,'to_step':1}]}
        with tempfile.TemporaryDirectory() as folder:
            report,_=Runner(self.session.bridge).run(document,folder)
            self.assertFalse(report['ok'])
            self.assertIn('Spawned',report['summary'][0])

    def test_no_event_after_match_sees_second_event_in_same_batch(self):
        document={'name':'duplicate','checks':'unit','steps':[
            {'call':'console','args':{'line':'paid twice'}},
            {'call':'console','args':{'line':'paid twice'}},
            {'wait_event':'paid twice','after_step':0},
            {'no_event':'paid twice','from_step':3}]}
        with tempfile.TemporaryDirectory() as folder:
            report,_=Runner(self.session.bridge).run(document,folder)
            self.assertFalse(report['ok'])
            self.assertEqual(report['steps'][-1]['status'],'FAIL')

    def test_expected_error_does_not_hide_connection_failure(self):
        class BrokenBridge:
            def health(self): return {'session':'broken','save':'FT unit','scene':'city'}
            def events(self, since=0): return {'session':'broken','events':[],'next':0,'dropped':0}
            def call(self, *args): raise BridgeError('No bridge: start the game')
        document={'name':'expected-failure','checks':'unit','steps':[{'call':'money','expect_ok':False}]}
        with tempfile.TemporaryDirectory() as folder:
            report,_=Runner(BrokenBridge()).run(document,folder)
            self.assertFalse(report['ok'])

    def test_validation_rejects_bad_schema(self):
        for step in [{},{'call':'time','note':'two'}, {'call':'x','args':[]}, {'expect':'state','path':'money'},
                     {'wait_event':'['},{'sleep_s':-1},{'call':'x','typo':True},{'shoot':{'name':'missing camera'}},
                     {'no_event':'x','to_step':10}]:
            with self.subTest(step=step),self.assertRaises(ValueError):validate({'name':'bad','checks':'unit','steps':[step]})


class MCPTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):cls.session=MockSession().__enter__()
    @classmethod
    def tearDownClass(cls):cls.session.__exit__(None,None,None)

    def test_stdio_initialize_list_and_calls(self):
        env=dict(os.environ,BA_BRIDGE_URL=self.session.url)
        env.pop('BA_GAME_HOST',None)
        process=subprocess.Popen(entry_command('ft-mcp'),cwd='/private/tmp',env=env,
                                 stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
        def request(method,params=None,identifier=1):
            process.stdin.write(json.dumps({'jsonrpc':'2.0','id':identifier,'method':method,'params':params or {}})+'\n')
            process.stdin.flush()
            return json.loads(line_from_pipe(process))
        try:
            init=request('initialize',{'protocolVersion':'2025-06-18','capabilities':{},'clientInfo':{'name':'unit','version':'1'}})
            self.assertEqual(init['result']['protocolVersion'],'2025-06-18')
            process.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n')
            process.stdin.flush()
            listed=request('tools/list')
            self.assertEqual(len(listed['result']['tools']),11)
            self.assertEqual(request('ping')['result'],{})
            for name,arguments in [('ft_health',{}),('ft_state',{'path':'money'}),('ft_call',{'cmd':'money','args':{'set':1234}}),
                                   ('ft_events',{'since':0,'grep':'Version'}),('ft_ui_list',{})]:
                reply=request('tools/call',{'name':name,'arguments':arguments})
                self.assertFalse(reply['result']['isError'],reply)
            request('tools/call',{'name':'ft_call','arguments':{'cmd':'time','args':{'speed':60}}})
            waited=request('tools/call',{'name':'ft_wait_event','arguments':{'regex':'Day summary:','timeout_s':2}})
            self.assertFalse(waited['result']['isError'],waited)
            request('tools/call',{'name':'ft_call','arguments':{'cmd':'time','args':{'pause':True}}})
            for action in ('spawn truck','licence'):
                request('tools/call',{'name':'ft_call','arguments':{'cmd':'debug','args':{'action':action}}})
            request('tools/call',{'name':'ft_call','arguments':{'cmd':'vehicle','args':{'id':'truck-1','op':'enter'}}})
            request('tools/call',{'name':'ft_call','arguments':{'cmd':'time','args':{'pause':False}}})
            clicked=request('tools/call',{'name':'ft_ui_click','arguments':{'text':'OPEN HERE'}})
            self.assertFalse(clicked['result']['isError'],clicked)
            with tempfile.TemporaryDirectory() as folder:
                log=Path(folder)/'Player.log'
                log.write_text('[Mod:FoodTrucks] Version 0.0.0 loading.\n')
                checked=request('tools/call',{'name':'ft_check_log','arguments':{'path':str(log)}})
                self.assertFalse(checked['result']['isError'])
            invalid_regex=request('tools/call',{'name':'ft_events','arguments':{'grep':'['}})
            self.assertTrue(invalid_regex['result']['isError'])
            error=request('tools/call',{'name':'ft_call','arguments':{'cmd':'wrong'}})
            self.assertTrue(error['result']['isError'])
            image=request('tools/call',{'name':'ft_shoot','arguments':{'name':'mcp-test','pose':'door','target':'commissary:hells-kitchen'}})
            content=image['result']['content']
            self.assertEqual(content[1]['type'],'image')
            self.assertTrue(base64.b64decode(content[1]['data']).startswith(b'\x89PNG'))
            self.assertTrue(Path(image['result']['structuredContent']['local_path']).exists())
            free=request('tools/call',{'name':'ft_shoot','arguments':{'name':'mcp-free','x':0,'y':1.65,'z':0,'look_at':{'x':0,'y':1.65,'z':10},'fov':50,'timeout_s':30}})
            self.assertFalse(free['result']['isError'],free)
            self.assertEqual(free['result']['structuredContent']['result']['camera']['fov'],50)
            mixed=request('tools/call',{'name':'ft_shoot','arguments':{'name':'mcp-mixed','x':0,'y':1,'z':0,'yaw':4,'look_at':{'x':1,'y':1,'z':1}}})
            self.assertTrue(mixed['result']['isError'])
            invalid=request('tools/call',{'name':'ft_ui_click','arguments':{}})
            self.assertTrue(invalid['result']['isError'])
            self.assertEqual(request('unknown')['error']['code'],-32601)
            process.stdin.write('broken JSON\n');process.stdin.flush()
            self.assertEqual(json.loads(line_from_pipe(process))['error']['code'],-32700)
        finally:
            process.stdin.close()
            process.wait(timeout=10)
            process.stdout.close()
            process.stderr.close()


class CLIAndShotsTests(unittest.TestCase):
    def test_cli_from_any_directory_and_value_parsing(self):
        with MockSession() as session:
            env=dict(os.environ,BA_BRIDGE_URL=session.url)
            def run(*args):
                return subprocess.run(entry_command('ft', *args),cwd='/private/tmp',env=env,capture_output=True,text=True,timeout=10)
            reply=run('--json','call','money','set=123.5')
            self.assertEqual(reply.returncode,0,reply.stderr)
            self.assertEqual(json.loads(reply.stdout)['result']['money'],123.5)
            reply=run('call','time','--json','{"pause":true}')
            self.assertEqual(reply.returncode,0,reply.stderr)
            self.assertIn('paused: true',reply.stdout)
            for line in (('call','money','set=77','--json'),('call','--json','money','set=77'),('call','money','--json','set=77'),('call','money','--args','{"set":77}','--json'),
                         ('call','money','set=1','--json','set=77'),('--json','call','money','set=77')):
                reply=run(*line)
                self.assertEqual(reply.returncode,0,(line,reply.stderr))
                self.assertEqual(json.loads(reply.stdout)['result']['money'],77,line)
            reply=run('call','teleport','who=player','--json','x=1','z=2')
            self.assertEqual(reply.returncode,0,reply.stderr)
            self.assertTrue(json.loads(reply.stdout)['ok'])
            # Both key=value arguments reach the bridge (the mock has no truck, so the reply is its refusal, as JSON).
            reply=run('call','vehicle','id=truck-1','--json','op=enter','--timeout','5')
            self.assertNotIn('unrecognized arguments',reply.stderr)
            self.assertIn("No food cart or truck with id 'truck-1'",json.loads(reply.stdout)['error'])
            # `call` as an argument of another subcommand is left alone.
            reply=run('wait-event','call','--timeout','0.2','--json')
            self.assertNotIn('unrecognized arguments',reply.stderr)
            reply=run('state','money','--json')
            self.assertIn('ok',json.loads(reply.stdout))
            reply=run('state','missing')
            self.assertEqual(reply.returncode,1)
            self.assertNotIn('Traceback',reply.stderr)
            reply=run('call','time','run_until.day=2','run_until.time=08:05','speed=20','--raw')
            self.assertEqual(reply.returncode,0,reply.stderr)
            self.assertTrue(json.loads(reply.stdout)['ok'])
            # A free camera from the command line (live session 4 had to send it as a raw call).
            reply=run('--json','shoot','free-cli','--at','1,1.7,2','--look-at','1,1.7,12','--fov','45','--width','16','--height','16','--timeout','15')
            self.assertEqual(reply.returncode,0,reply.stderr)
            camera=json.loads(reply.stdout)['result']['camera']
            self.assertEqual((camera['x'],camera['z'],camera['yaw'],camera['fov']),(1,2,0,45))
            reply=run('--json','shoot','free-yaw','--at','1,1.7,2','--yaw','90','--pitch','10','--width','16','--height','16')
            self.assertEqual(json.loads(reply.stdout)['result']['camera']['yaw'],90)
            with tempfile.TemporaryDirectory() as folder:
                reply=run('shoot','fetched','--at','1,1.7,2','--yaw','90','--width','16','--height','16','--fetch',folder,'--json')
                self.assertEqual(reply.returncode,0,reply.stderr)
                result=json.loads(reply.stdout)['result']
                self.assertTrue(Path(result['local_path']).exists())
            for bad in (('shoot','x'),('shoot','x','--pose','door'),('shoot','x','--at','1,2'),('shoot','x','--at','1,2,3','--pose','door','--target','commissary:hells-kitchen'),
                        ('shoot','x','--at','1,2,3','--look-at','1,2,4','--yaw','3')):
                reply=run(*bad)
                self.assertEqual(reply.returncode,1,bad)
                self.assertNotIn('Traceback',reply.stderr)

    def test_local_shot_and_offline_sheet(self):
        with MockSession() as session,tempfile.TemporaryDirectory() as folder:
            remote=session.bridge.call('shoot',{'name':'test','pose':'door','target':'commissary:hells-kitchen'})['result']['path']
            local=fetch_shot(remote,folder,'a<&>.png')
            Path(folder,'report.json').write_text(json.dumps({'steps':[{'step':3,'shot':str(local),'note':'<Review>'}]}))
            page=contact_sheet(folder).read_text()
            self.assertIn('data:image/png;base64,',page)
            self.assertIn('Step 3: &lt;Review&gt;',page)
            self.assertNotIn('https:',page)
            self.assertIn('color-scheme:light dark',page)

    def test_remote_fetch_builds_scp_argument_without_shell(self):
        with tempfile.TemporaryDirectory() as folder:
            def fake_scp(args,**kwargs):
                self.assertEqual(args[:2],['scp','--'])
                self.assertEqual(args[2],'homepc:C:/Users/Peter/shots/a b.png')  # Windows OpenSSH wants forward slashes
                self.assertNotIn('shell',kwargs)
                Path(args[-1]).write_bytes(b'\x89PNG\r\n\x1a\nmock')
                return subprocess.CompletedProcess(args,0,'','')
            with patch.dict(os.environ,{'BA_GAME_HOST':'homepc'}),patch('ftlib.shots.subprocess.run',side_effect=fake_scp):
                self.assertTrue(fetch_shot(r'C:\Users\Peter\shots\a b.png',folder).exists())


if __name__=='__main__':unittest.main()
