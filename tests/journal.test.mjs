import test from 'node:test';
import assert from 'node:assert/strict';
import {journalView} from '../prototype/journal-view.mjs';
import {TextView} from '../prototype/text-view.mjs';
import {createBridge} from '../bridge/server.mjs';

const journal={day:6,today:[{name:'日用品救济行动',effect:'-40% 日用品'}],
  rent:{kind:'rent',days_until_due:1,due_day:7,amount:200},
  difficulty:{name:'普通',endless:false},traits:[{name:'幸运'}],
  reputation:[{name:'革命军',value:27.31,status:'小有名气'}],black_market:{unlocked:false}};
const snapshot={context_id:'load-6',place:'店里',day_label:'第6天',displayed_cash:589,journal};

test('calendar context stays quiet until something changes and a new save replaces it',()=>{
  const view=new TextView();const initial=view.observe(snapshot)[0].text;
  for(const text of ['-40% 日用品','明天要交租金 200¢','革命军 27.31','幸运','黑市尚未解锁'])assert.ok(initial.includes(text));
  assert.deepEqual(view.observe(structuredClone(snapshot)),[]);
  const changed=view.observe({...snapshot,journal:{...journal,reputation:[{name:'革命军',value:28,status:'小有名气'}]}})[0].text;
  assert.match(changed,/革命军 28/);assert.ok(!changed.includes('-40%'));
  const next=view.observe({...snapshot,context_id:'new-save',journal:{day:1,traits:[],today:[],black_market:{unlocked:false}}})[0].text;
  assert.ok(!next.includes('27.31'));assert.ok(!next.includes('幸运'));
});
test('locked black market has no offers and detailed native descriptions survive the short scene limit',()=>{
  assert.equal(journalView({topic:'black_market',unlocked:false,text:['不能看到的兑换']}),'这个存档的黑市尚未解锁。');
  const description='这条原生天赋说明很长。'.repeat(65);
  const text=journalView({topic:'status',traits:[{name:'养殖达人',description}],cash:589,estimated_total_assets:2448});
  assert.ok(text.includes(description));assert.match(text,/2,448¢/);
  const calendar=journalView({topic:'calendar',day:6,days:[{day:7,events:[{name:'安租日',effect:''}]}],rent:journal.rent});
  assert.match(calendar,/第7天：安租日/);
});
test('journal tools cross the same game command/receipt path and retain their readable view',async t=>{
  const bridge=createBridge({token:'journal-test-only'});t.after(()=>bridge.close());await bridge.listen();
  const packet={protocol_version:'0.1',game_instance_id:'game-6',context_id:'load-6',active:true,snapshot};
  await bridge.sync(packet);const binding=await bridge.call('companion_connect',{game_instance_id:'game-6'});
  const args={binding_id:binding.binding_id,context_id:'load-6',action_id:'journal-1',topic:'overview',wait_ms:0};
  assert.equal((await bridge.call('companion_read_journal',args)).status,'queued');
  assert.equal((await bridge.sync({...packet,snapshot:null})).commands[0].action,'read_journal');
  await bridge.sync({...packet,snapshot:null,receipts:[{command_id:'journal-1',status:'observed',detail:{...journal,topic:'overview'}}]});
  const receipt=await bridge.call('companion_receipt',{binding_id:binding.binding_id,action_id:'journal-1'});
  assert.match(receipt.view,/黑市尚未解锁/);assert.equal(receipt.detail.reputation[0].value,27.31);
  await assert.rejects(bridge.call('companion_open_journal',{...args,action_id:'bad',topic:'hidden_future'}),{code:'invalid_request'});
});
