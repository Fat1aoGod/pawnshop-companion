import test from 'node:test';
import assert from 'node:assert/strict';
import {createBridge} from '../bridge/server.mjs';
import {callBridge} from '../harness/client.mjs';

const snapshot={context_id:'load-1',place:'店里',player_name:'Fat1ao',displayed_cash:491,
  customer:{visible:true,visit_id:'visit-1',display_name:'顾客',displayed_dialogue:'你好。'}};
const hello=(extra={})=>({protocol_version:'0.1',game_instance_id:'game-1',context_id:'load-1',active:true,snapshot,...extra});
async function fixture(t){const config={token:'test-pairing-only'};const bridge=createBridge(config);const address=await bridge.listen();config.port=address.port;t.after(()=>bridge.close());return {bridge,config};}

test('real HTTP game sync -> text view -> player message -> game acknowledgement',async t=>{
  const {bridge,config}=await fixture(t);
  async function sync(body){const response=await fetch(`http://127.0.0.1:${config.port}/game/sync`,{method:'POST',headers:{authorization:`Bearer ${config.token}`,'content-type':'application/json'},body:JSON.stringify(body)});assert.equal(response.status,200);return response.json();}
  await sync(hello());
  const games=await callBridge('companion_list_games',{},config);assert.equal(games.games.length,1);
  const binding=await callBridge('companion_connect',{game_instance_id:'game-1',display_name:'Codex'},config);
  assert.match(binding.view,/491/);
  const args={binding_id:binding.binding_id,context_id:'load-1',action_id:'reply-1',text:'我来陪你看店。'};
  const waiting=callBridge('companion_say',args,config);
  await new Promise(resolve=>setTimeout(resolve,30));
  const poll=await sync(hello({snapshot:null,events:[{id:'p1',type:'player_message',text:'先聊聊？'}]}));
  assert.equal(poll.commands.length,1);assert.equal(poll.commands[0].text,args.text);
  await sync(hello({snapshot:null,receipts:[{command_id:'reply-1',status:'displayed',summary:'消息已到聊天栏。'}]}));
  assert.equal((await waiting).status,'displayed');
  const events=await callBridge('companion_wait_events',{binding_id:binding.binding_id,after_cursor:binding.cursor,timeout_ms:0},config);
  assert.ok(events.events.some(e=>e.kind==='player_message' && e.text==='先聊聊？'));
  assert.equal((await bridge.sync(hello({snapshot:null}))).commands.length,0);
});

test('one action id is applied once; retries return its receipt and changed payload is rejected',async t=>{
  const {bridge}=await fixture(t);await bridge.sync(hello());
  const binding=await bridge.call('companion_connect',{game_instance_id:'game-1'});
  const args={binding_id:binding.binding_id,context_id:'load-1',action_id:'swap-1',installed_ref:'b1',replacement_ref:'b2',wait_ms:0};
  assert.equal((await bridge.call('companion_swap_battery',args)).status,'queued');
  const received=await bridge.sync(hello({snapshot:null}));assert.equal(received.commands.length,1);
  await bridge.sync(hello({snapshot:null,receipts:[{command_id:'swap-1',status:'applied',summary:'电池换好了，旧电池也留着。'}]}));
  assert.equal((await bridge.call('companion_swap_battery',args)).status,'applied');
  assert.equal((await bridge.sync(hello({snapshot:null}))).commands.length,0);
  await assert.rejects(bridge.call('companion_swap_battery',{...args,replacement_ref:'b3'}),{code:'id_conflict'});
});

test('load change and stopping play cancel pending operations; stale requests do not enter new load',async t=>{
  const {bridge}=await fixture(t);await bridge.sync(hello());const binding=await bridge.call('companion_connect',{game_instance_id:'game-1'});
  const args={binding_id:binding.binding_id,context_id:'load-1',action_id:'sort-1',storage_ref:'storage-main',wait_ms:0};
  await bridge.call('companion_sort_storage',args);
  const changed=await bridge.sync(hello({context_id:'load-2',snapshot:{...snapshot,context_id:'load-2'}}));assert.equal(changed.commands.length,0);
  await assert.rejects(bridge.call('companion_sort_storage',args),{code:'stale_context'});
  await bridge.sync(hello({context_id:'load-2',snapshot:null,active:false}));
  await assert.rejects(bridge.call('companion_read_state',{binding_id:binding.binding_id}),{code:'not_connected'});
});

test('browser origins and unpaired callers cannot read or control the local game',async t=>{
  const {config}=await fixture(t);const address=`http://127.0.0.1:${config.port}/health`;
  assert.equal((await fetch(address)).status,403);
  assert.equal((await fetch(address,{headers:{authorization:`Bearer ${config.token}`,origin:'https://example.com'}})).status,403);
});
