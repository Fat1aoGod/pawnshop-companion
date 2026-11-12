import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {setTimeout as pause} from 'node:timers/promises';
import {CompanionSession} from '../lib/session.mjs';
import {makeBridge} from '../lib/bridge.mjs';
import {execFileSync} from 'node:child_process';

const pluginRoot=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const local={projectRoot:path.resolve(pluginRoot,'../..'),displayName:'DeepSeek 搭子',avatarKey:'deepseek'};
const {createBridge}=await import(pathToFileURL(path.join(local.projectRoot,'bridge/server.mjs')).href);
async function until(check,limit=2500){const end=Date.now()+limit;while(Date.now()<end){if(await check())return;await pause(10);}throw new Error('test timed out');}
async function fixture(t,{available=true,waiting=false,saved=null}={}){
  const dir=await fs.mkdtemp(path.join(os.tmpdir(),'pawnshop-dsh-test-'));
  const native=createBridge({token:'isolated-test-only'});const address=await native.listen();
  await fs.mkdir(path.join(dir,'runtime'));await fs.writeFile(path.join(dir,'runtime/connection.json'),JSON.stringify({port:address.port,token:'isolated-test-only'}));
  const stateFile=path.join(dir,'binding.json');if(saved)await fs.writeFile(stateFile,JSON.stringify(saved));
  let snapshot={context_id:'load-1',place:'店里',day_label:'第6天',displayed_cash:589,customer:null,counter:[]};
  const sync=async(extra={})=>{if(extra.snapshot)snapshot=extra.snapshot;return native.sync({protocol_version:'0.1',game_instance_id:'game',context_id:snapshot.context_id,active:true,snapshot,...extra});};
  await sync(waiting?{events:[{id:'before',type:'player_message',text:'先发的玩家消息'}]}:{});
  const messages=[],injected=[],steered=[];
  const agent={status:'idle',id:'fake-session',followup:m=>{messages.push(m);},inject:m=>{injected.push(m);},steer:m=>{steered.push(m);}};
  const recipient={available};
  const cfg={...local,projectRoot:dir,waitTimeoutMs:100,reconnectDelayMs:20,observationCooldownMs:30000,autoStart:true};
  const client=makeBridge(cfg);
  const session=new CompanionSession({bridge:client,agentFor:()=>recipient.available?agent:null,config:cfg,stateFile});
  t.after(async()=>{
    await session.dispose();await native.close();
    const resolved=await fs.realpath(dir),parent=path.resolve(os.tmpdir())+path.sep;
    if(resolved.startsWith(parent)&&path.basename(resolved).startsWith('pawnshop-dsh-test-'))await fs.rm(resolved,{recursive:true,force:true});
  });
  return {dir,native,sync,session,client,agent,recipient,messages,injected,steered,get snapshot(){return snapshot;},stateFile};
}
const text=messages=>messages.map(m=>m.content.map(c=>c.text).join('\n')).join('\n');

test('actual installed DSH v4 validator accepts emitted messages and rejects retired plugin wrappers', {skip:!process.env.DSH_V4_EXECUTABLE},async t=>{
  const f=await fixture(t,{waiting:true});f.session.resume();await until(()=>f.messages.length===1);
  const exe=process.env.DSH_V4_EXECUTABLE;
  const parser=path.join(path.dirname(exe),'resources/app.asar/dsh/node_modules/@deepseek-ai/dsh-session-format-v3-to-v4/lib/index.js');
  const script=`const assert=require('node:assert/strict');const {assertV4RowAdmission}=require(process.argv[1]);const messages=JSON.parse(require('node:fs').readFileSync(0,'utf8'));for(const message of messages)assertV4RowAdmission({type:'user/message',data:message});assert.throws(()=>assertV4RowAdmission({type:'user/message',data:{...messages[0],source:{kind:'plugin',plugin:'old'}}}),/producer-owned source kind/);process.stdout.write('accepted:'+messages.length);`;
  const result=execFileSync(exe,['--expose-internals','-e',script,parser],{env:{...process.env,ELECTRON_RUN_AS_NODE:'1'},input:JSON.stringify([...f.messages,...f.injected]),encoding:'utf8',windowsHide:true,timeout:15000});
  assert.equal(result,'accepted:2');
});

test('first scene supplies required ids; queued player messages survive an absent Agent and same-text repeats',async t=>{
  const f=await fixture(t,{available:false,waiting:true});f.session.resume();
  await until(()=>f.session.players.length===1);assert.equal(f.messages.length,0);
  f.recipient.available=true;await f.session.flush();
  assert.match(text(f.injected),new RegExp(f.session.binding.binding_id));assert.match(text(f.injected),/context_id=load-1/);
  assert.equal(f.injected.length,1);assert.equal(f.messages.length,1);assert.equal(f.messages[0].source.kind,'user');
  await f.sync({snapshot:null,events:[{id:'again-1',type:'player_message',text:'再看一下'},{id:'again-2',type:'player_message',text:'再看一下'}]});
  await until(()=>f.messages.filter(m=>text([m]).includes('再看一下')).length===2);
  assert.equal(new Set(f.messages.map(m=>m.id)).size,f.messages.length);
});
test('cooldown coalesces full scenes, and a player question receives the latest customer dialogue immediately',async t=>{
  const f=await fixture(t);f.session.resume();await until(()=>f.messages.length>0);
  const before=f.messages.length;
  await f.sync({snapshot:{...f.snapshot,displayed_cash:590}});await pause(50);
  assert.equal(f.messages.length,before);
  await f.sync({snapshot:{...f.snapshot,customer:{visible:true,visit_id:'v',display_name:'顾客',displayed_dialogue:'今天只收干净的水'}},
    events:[{id:'question',type:'player_message',text:'顾客刚才说什么？'}]});
  await until(()=>text(f.messages).includes('顾客刚才说什么'));
  assert.match(text(f.injected),/今天只收干净的水/);assert.equal(f.session.players.length,0);
});
test('explicit stop stays stopped, then an explicit start reuses the normal event lifecycle',async t=>{
  const f=await fixture(t);f.session.resume();await until(()=>Boolean(f.session.binding));
  const previous=f.session.binding.binding_id;await f.session.stop(previous);await pause(180);
  assert.equal((await f.native.call('companion_list_games')).games[0].bound_to,null);
  assert.equal(f.session.binding,null);assert.equal(JSON.parse(await fs.readFile(f.stateFile)).enabled,false);
  await f.session.start({game_instance_id:'game',display_name:'DeepSeek 搭子'});
  assert.ok(f.session.binding);assert.notEqual(f.session.binding.binding_id,previous);
  await f.sync({snapshot:null,events:[{id:'after-restart',type:'player_message',text:'又回来啦'}]});
  await until(()=>text(f.messages).includes('又回来啦'));
});
test('invalid persisted binding recovers automatically; changed save rejects old actions and supplies the new context',async t=>{
  const f=await fixture(t,{saved:{enabled:true,binding:{binding_id:'obsolete',context_id:'old-load'},cursor:8}});
  f.session.resume();await until(()=>f.messages.length>0);
  const binding=f.session.binding.binding_id;assert.notEqual(binding,'obsolete');
  await f.sync({snapshot:{...f.snapshot,context_id:'load-2',day_label:'第1天',displayed_cash:600}});
  await until(()=>f.session.binding?.context_id==='load-2' && text(f.messages).includes('context_id=load-2'));
  await assert.rejects(f.session.call('companion_read_journal',{binding_id:binding,context_id:'load-1',action_id:'old',topic:'status'}),{code:'stale_context'});
});
test('busy observations are injected once with plugin provenance; followups are reserved for real player messages',async t=>{
  const f=await fixture(t);f.agent.status='running';f.session.resume();await until(()=>f.injected.length===1);
  assert.equal(f.messages.length,0);assert.equal(f.injected[0].source.kind,'user');
  await f.sync({snapshot:null,events:[{id:'busy-player',type:'player_message',text:'搭把手'}]});
  await until(()=>f.steered.length===1);assert.equal(f.steered[0].source.kind,'user');assert.equal(f.messages.length,0);
  assert.equal(f.session.players.length,0);
});
test('a connection completing after stop is released without delivering any message',async t=>{
  let finish,started;
  const began=new Promise(resolve=>{started=resolve;});const gate=new Promise(resolve=>{finish=resolve;});
  const dir=await fs.mkdtemp(path.join(os.tmpdir(),'pawnshop-dsh-race-'));t.after(async()=>{const p=await fs.realpath(dir);if(p.startsWith(path.resolve(os.tmpdir())+path.sep)&&path.basename(p).startsWith('pawnshop-dsh-race-'))await fs.rm(p,{recursive:true,force:true});});
  const calls=[],messages=[];
  const bridge={ensure:async()=>{},async call(name){calls.push(name);if(name==='companion_list_games')return {games:[{game_instance_id:'game'}]};if(name==='companion_connect'){started();await gate;return {binding_id:'late',context_id:'load',cursor:0};}return {status:'disconnected'};}};
  const session=new CompanionSession({bridge,agentFor:()=>({followup:m=>messages.push(m)}),config:{autoStart:true},stateFile:path.join(dir,'state.json')});
  session.resume();await began;const stopping=session.stop();finish();await stopping;
  assert.ok(calls.includes('companion_disconnect'));assert.equal(messages.length,0);assert.equal(session.binding,null);
});
test('tools register only in the selected Agent scope, reject another caller, and unregister on unload',async t=>{
  const f=await fixture(t);const dir=path.join(f.dir,'plugin');await fs.mkdir(path.join(dir,'lib'),{recursive:true});
  for(const name of ['index.js','package.json','tools.json','lib/bridge.mjs','lib/session.mjs'])await fs.copyFile(path.join(pluginRoot,name),path.join(dir,name));
  await fs.writeFile(path.join(dir,'config.json'),JSON.stringify({...local,projectRoot:f.dir,sessionId:'target',autoStart:false}));
  const registered=new Map(),effects=[],events=new Map();
  f.agent.ctx={on:()=>()=>{},tools:{register(spec){registered.set(spec.name,spec);return ()=>registered.delete(spec.name);}}};
  const ctx={agents:{get:id=>id==='target'?f.agent:null},tools:{register(){throw new Error('global registration');}},
    on:(event,handler)=>events.set(event,handler),effect:fn=>effects.push(fn())};
  const mod=await import(pathToFileURL(path.join(dir,'index.js')).href);mod.apply(ctx);
  assert.equal(registered.size,26);assert.equal(registered.has('companion_wait_events'),true);
  await assert.rejects(f.session.call('companion_wait_events',{}),{code:'host_managed_wait'});
  const list=registered.get('companion_list_games');assert.equal((await list.execute({}, {agent:{id:'other'}})).error,'wrong_session');
  assert.equal((await list.execute({}, {agent:f.agent})).games.length,1);
  await registered.get('companion_connect').execute({game_instance_id:'game',display_name:'DeepSeek 搭子'},{agent:f.agent});
  let concluded=0;
  const waited=await registered.get('companion_wait_events').execute({timeout_ms:5},{agent:f.agent,concludeTurn:()=>{concluded++;}});
  assert.equal(waited.status,'idle');assert.equal(concluded,1);
  for(const dispose of effects)await dispose();assert.equal(registered.size,0);
});

test('model wait uses the host inbox and a player event is returned exactly once without a second followup',async t=>{
  const f=await fixture(t);f.session.resume();await until(()=>f.messages.length===1);
  f.agent.status='running';const before=f.messages.length;
  const waiting=f.session.waitForEvents({timeout_ms:2000});
  await f.sync({snapshot:null,events:[{id:'wait-player',type:'player_message',text:'陪我等一下'}]});
  const result=await waiting;
  assert.equal(result.events.filter(e=>e.kind==='player_message').length,1);
  assert.equal(result.events[0].text,'陪我等一下');assert.equal(f.messages.length,before);assert.equal(f.steered.length,0);
  await f.sync({snapshot:null,events:[{id:'wait-player',type:'player_message',text:'陪我等一下'}]});
  await pause(80);assert.equal(f.steered.length,0);assert.equal(f.session.players.length,0);
});

test('unchanged scene and routing ids are not repeated with every player message',async t=>{
  const f=await fixture(t);f.session.resume();await until(()=>f.messages.length===1);
  await f.sync({snapshot:null,events:[{id:'short-player',type:'player_message',text:'日用品先不卖'}]});
  await until(()=>f.messages.length===2);
  assert.match(text([f.messages[1]]),/日用品先不卖/);assert.ok(!text([f.messages[1]]).includes('binding_id='));
  assert.equal(f.injected.length,0);
});

test('waiting returns only changed scene lines; idle timeout leaves later player messages for normal delivery',async t=>{
  const f=await fixture(t);f.session.resume();await until(()=>f.messages.length===1);
  f.agent.status='running';const waiting=f.session.waitForEvents({timeout_ms:2000});
  await f.sync({snapshot:{...f.snapshot,displayed_cash:590}});
  const result=await waiting;assert.match(result.events[0].text,/590/);assert.ok(!result.events[0].text.includes('binding_id='));
  const idle=await f.session.waitForEvents({timeout_ms:10});assert.equal(idle.status,'idle');
  f.agent.status='idle';await f.sync({snapshot:null,events:[{id:'after-idle',type:'player_message',text:'回来了'}]});
  await until(()=>text(f.messages).includes('回来了'));assert.equal(f.session.players.length,0);
});

test('external inbox input and cancellation release the wait without consuming later game messages',async t=>{
  const f=await fixture(t);f.session.resume();await until(()=>f.messages.length===1);
  const waiting=f.session.waitForEvents({timeout_ms:2000});f.session.interruptWait();assert.equal((await waiting).status,'inbox');
  const abort=new AbortController();const cancelled=f.session.waitForEvents({timeout_ms:2000},abort.signal);
  abort.abort();await assert.rejects(cancelled);assert.equal(f.session.waiter,null);
  await f.sync({snapshot:null,events:[{id:'after-cancel',type:'player_message',text:'还在吗'}]});
  await until(()=>text(f.messages).includes('还在吗'));
});
