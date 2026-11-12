import test from 'node:test';
import assert from 'node:assert/strict';
import {TextView} from './text-view.mjs';

const base = {context_id: 'load-1', place: '店里', day_label: '第 4 天',
  displayed_cash: 491, customer: null, counter: [], actions: []};
test('unchanged scenes stay silent, including changes to hidden state', () => {
  const view = new TextView();
  view.observe(base);
  for (let i = 0; i < 100; i++) assert.deepEqual(view.observe({...base, hidden_rng: i}), []);
});
test('only public text survives; hidden identities, budgets and item values never enter narration', () => {
  const scene = {...base, secret_future_dialogue: 'future-secret',
    customer: {visible: true, visit_id: 'v1', display_name: '顾客', isAug: true,
      true_identity: 'identity-secret', private_budget: 123456, displayed_dialogue: '你好'},
    counter: [{visible: true, display_name: '未鉴定物品', hidden_base_value: 998877},
      {visible: false, display_name: 'secret-inventory'}]};
  const text = new TextView().observe(scene)[0].text;
  assert.match(text, /未鉴定物品/);
  for (const secret of ['future-secret', 'identity-secret', '123456', '998877', 'secret-inventory', '伪人'])
    assert.ok(!text.includes(secret));
});
test('the same player utterance on two distinct occasions is retained; retransmissions are suppressed', () => {
  const view = new TextView(); view.observe(base);
  const message = {id: 'p1', type: 'player_message', text: '我不太确定\n你怎么看？'};
  assert.equal(view.observe(base, [message])[0].text, message.text);
  assert.deepEqual(view.observe(base, [message]), []);
  assert.equal(view.observe(base, [{...message, id: 'p2'}])[0].text, message.text);
});
test('cash movement is not invented profit, and a trade is described from the player perspective', () => {
  const view = new TextView(); view.observe(base);
  const text = view.observe({...base, displayed_cash: 451})[0].text;
  assert.match(text, /491¢ → 451¢/);
  assert.ok(!/[亏赚]/u.test(text));
  const trade = view.observe({...base, displayed_cash: 451}, [{id: 't1', type: 'trade_completed',
    public_result: {player_action: 'bought', display_item_name: '旧手枪', displayed_amount: 40}}])[0].text;
  assert.match(trade, /花 40¢ 收下/);
});
test('context reset gives a new view; default view is short and details are requested explicitly', () => {
  const scene = {...base, counter: Array.from({length: 12}, (_, i) => ({visible: true, display_name: `物品${i}`, display_quantity: 1}))};
  const view = new TextView();
  const text = view.observe(scene)[0].text;
  assert.ok([...text].length <= 700); assert.ok(!text.includes('物品11'));
  assert.match(view.read(scene, {focus: 'counter', detailed: true}), /物品11/);
  assert.equal(view.observe({...scene, context_id: 'load-2'}).length, 1);
});
test('queued operations do not appear complete; swapped battery does not invent a running machine', () => {
  const scene = {...base, devices: [{visible: true, ref: 'm1', display_name: '净水器', displayed_status: '没电，停着'}]};
  const view = new TextView(); view.observe(scene);
  assert.deepEqual(view.observe(scene, [{id: 'q1', type: 'action_result', receipt: {status: 'queued', summary: '已恢复工作'}}]), []);
  const changed = view.observe(scene, [{id: 'a1', type: 'action_result', actor: 'DeepSeek 搭子',
    receipt: {action: 'swap_battery', status: 'applied', summary: '电池换好了，旧电池也留着。'}}])[0].text;
  assert.match(changed, /DeepSeek 搭子搭了把手/);
  assert.ok(!changed.includes('恢复工作'));
  assert.match(view.read(scene, {focus: 'power'}), /没电，停着/);
  const resumed = view.observe({...base, devices: [{visible: true, ref: 'm1', display_name: '净水器', displayed_status: '正在工作'}]})[0].text;
  assert.match(resumed, /正在工作/);
});
