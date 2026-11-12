// Local, deterministic text view. Input is the game's public observation,
// not screenshots, save data, an NPC's true identity, or model output.
import {journalSummary} from './journal-view.mjs';
const clean = value => typeof value === 'string' ? value.replace(/\s+/gu, ' ').trim() : '';
const shorten = (value, length) => {
  const points = [...value];
  return points.length > length ? points.slice(0, length - 1).join('') + '…' : value;
};
const money = value => Number.isFinite(value) ? `${value.toLocaleString('zh-CN')}¢` : null;
const equal = (left, right) => JSON.stringify(left) === JSON.stringify(right);

function publicView(snapshot) {
  const visibleCustomer = snapshot.customer?.visible === true ? snapshot.customer : null;
  const panel = snapshot.panel?.visible === true ? snapshot.panel : null;
  return {
    context: snapshot.context_id,
    place: clean(snapshot.place), day: clean(snapshot.day_label), player: shorten(clean(snapshot.player_name) || '玩家', 30),
    surroundings: (snapshot.surroundings ?? []).filter(item => item.visible === true)
      .map(item => clean(item.text)).filter(Boolean).slice(0, 2),
    customer: visibleCustomer ? {
      visit: visibleCustomer.visit_id,
      name: clean(visibleCustomer.display_name),
      appearance: clean(visibleCustomer.observed_appearance),
      dialogue: clean(visibleCustomer.displayed_dialogue),
    } : null,
    counter: Array.isArray(snapshot.counter) ? snapshot.counter.filter(item => item.visible === true)
      .map(item => ({name: clean(item.display_name), quantity: item.display_quantity,
        detail: clean(item.displayed_detail)})).filter(item => item.name) : null,
    cash: Number.isFinite(snapshot.displayed_cash) ? snapshot.displayed_cash : null,
    journal: snapshot.journal ? journalSummary(snapshot.journal) : [],
    devices: (snapshot.devices ?? []).filter(device => device.visible === true)
      .map(device => ({ref: device.ref, name: clean(device.display_name), status: clean(device.displayed_status)}))
      .filter(device => device.name && device.status),
    panel: panel ? {title: clean(panel.display_title), body: clean(panel.displayed_text)} : null,
    actions: (snapshot.actions ?? []).filter(item => item.visible === true && item.enabled === true)
      .map(item => clean(item.label)).filter(Boolean).slice(0, 4),
  };
}

function customerLine(customer) {
  if (!customer) return '窗口前暂时没有顾客。';
  const name = customer.name ? `（${shorten(customer.name, 30)}）` : '';
  return customer.appearance ? `窗口前是${shorten(customer.appearance, 80)}${name}。`
    : customer.name ? `「${shorten(customer.name, 30)}」在窗口前。` : '有位顾客站在窗口前。';
}

function dialogueLine(customer) {
  return customer?.dialogue ? `顾客说：「${shorten(customer.dialogue, 180)}」` : null;
}

function counterLine(counter, maxItems = 4) {
  if (counter === null) return null;
  if (counter.length === 0) return '柜台上暂时没有物品。';
  const visible = counter.slice(0, maxItems).map(item => {
    const count = Number.isFinite(item.quantity) && item.quantity > 1 ? ` ×${item.quantity}` : '';
    const detail = item.detail ? `（${shorten(item.detail, 55)}）` : '';
    return `${shorten(item.name, 40)}${count}${detail}`;
  });
  const rest = counter.length > maxItems ? `；另外还有 ${counter.length - maxItems} 项，想看时可以展开` : '';
  return `柜台：${visible.join('；')}${rest}。`;
}

function panelLine(panel, player) {
  if (!panel) return null;
  return `${player}正看着${panel.title ? `「${shorten(panel.title, 50)}」` : '一个窗口'}${panel.body ? `：${shorten(panel.body, 240)}` : '。'}`;
}

function deviceLine(devices) {
  if (!devices.length) return null;
  return `机器这边：${devices.slice(0, 4).map(device => `${shorten(device.name, 40)}（${shorten(device.status, 70)}）`).join('；')}。`;
}

function actionLine(event) {
  const receipt = event.receipt;
  if (!receipt || !['applied', 'busy', 'blocked', 'unavailable'].includes(receipt.status)) return null;
  const text = shorten(clean(receipt.summary), 160);
  if (!text) return null;
  return receipt.status === 'applied' ? `${clean(event.actor) || '伙伴'}搭了把手：${text}` : `手边情况：${text}`;
}

function fullLines(view, maxItems = 4) {
  return [view.surroundings.join(' '), customerLine(view.customer), dialogueLine(view.customer),
    counterLine(view.counter, maxItems), view.cash === null ? null : `${view.player}手里有 ${money(view.cash)}。`,
    ...view.journal, deviceLine(view.devices), panelLine(view.panel, view.player), view.actions.length ? `眼下可选：${view.actions.join(' / ')}。` : null];
}

function tradeLine(event, player) {
  // Values must come from an actual public game result, never an appraisal estimate.
  const trade = event.public_result;
  if (!trade) return null;
  const item = clean(trade.display_item_name);
  const price = money(trade.displayed_amount);
  if (trade.player_action === 'bought' && item && price) return `${player}花 ${price} 收下了「${shorten(item, 50)}」。`;
  if (trade.player_action === 'sold' && item && price) return `${player}把「${shorten(item, 50)}」卖了 ${price}。`;
  return '这笔交易成交了。';
}

export class TextView {
  constructor({maxSceneChars = 700} = {}) {
    this.previous = null;
    this.maxSceneChars = maxSceneChars;
    this.seenEventIds = new Set();
  }

  read(snapshot, {focus = 'scene', detailed = false} = {}) {
    const view = publicView(snapshot);
    let lines;
    if (focus === 'counter') lines = [counterLine(view.counter, detailed ? 16 : 4)];
    else if (focus === 'customer') lines = [customerLine(view.customer), dialogueLine(view.customer)];
    else if (focus === 'panel') lines = [panelLine(view.panel, view.player) ?? `${view.player}目前没有打开详情窗口。`];
    else if (focus === 'devices' || focus === 'power') lines = [deviceLine(view.devices) ?? '目前没有读到这些机器的公开状态。'];
    else lines = fullLines(view, detailed ? 16 : 4);
    return this.format(view, lines, detailed ? 1800 : this.maxSceneChars);
  }

  observe(snapshot, events = []) {
    const next = publicView(snapshot);
    if (this.previous?.context !== next.context) this.seenEventIds.clear();
    const fresh = events.filter(event => {
      if (event.id && this.seenEventIds.has(event.id)) return false;
      if (event.id) this.seenEventIds.add(event.id);
      return true;
    });
    while (this.seenEventIds.size > 256) this.seenEventIds.delete(this.seenEventIds.values().next().value);
    const packets = [];
    let lines = [];
    const before = this.previous;
    if (!before || before.context !== next.context) lines = fullLines(next);
    else {
      if (before.place !== next.place || before.day !== next.day || !equal(before.surroundings, next.surroundings))
        lines.push(next.surroundings.join(' '));
      const a = before.customer, b = next.customer;
      if (a?.visit !== b?.visit || a?.name !== b?.name || a?.appearance !== b?.appearance)
        lines.push(customerLine(b));
      if (a?.visit !== b?.visit || a?.dialogue !== b?.dialogue) lines.push(dialogueLine(b));
      if (!equal(before.counter, next.counter)) lines.push(counterLine(next.counter));
      if (before.cash !== next.cash && next.cash !== null)
        lines.push(before.cash === null ? `${next.player}手里有 ${money(next.cash)}。` : `${next.player}的现金：${money(before.cash)} → ${money(next.cash)}。`);
      if (!equal(before.panel, next.panel)) lines.push(next.panel ? panelLine(next.panel, next.player) : '刚才的详情窗口关上了。');
      if (!equal(before.journal, next.journal)) lines.push(...next.journal.filter(line=>!before.journal.includes(line)));
      if (!equal(before.devices, next.devices)) {
        const changed = next.devices.filter(device => !before.devices.some(old => equal(device, old)));
        lines.push(deviceLine(changed));
      }
      if (!equal(before.actions, next.actions) && next.actions.length) lines.push(`眼下可选：${next.actions.join(' / ')}。`);
    }
    for (const event of fresh) if (event.type === 'trade_completed') lines.push(tradeLine(event, next.player));
    for (const event of fresh) if (event.type === 'action_result') lines.push(actionLine(event));
    lines = lines.filter(Boolean);
    if (lines.length) packets.push({kind: 'observation', text: this.format(next, lines),
      context_id: next.context, visit_id: next.customer?.visit ?? null});
    for (const event of fresh) if (event.type === 'player_message' && typeof event.text === 'string')
      packets.push({kind: 'player_message', text: event.text, context_id: next.context,
        visit_id: next.customer?.visit ?? null});
    this.previous = next;
    return packets;
  }

  format(view, lines, limit = this.maxSceneChars) {
    const header = `【${view.place || '当铺'}${view.day ? ` · ${view.day}` : ''}】`;
    return shorten([header, ...lines.filter(Boolean)].join('\n'), limit);
  }
}
