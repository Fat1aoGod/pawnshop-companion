const clean=value=>typeof value==='string'?value.replace(/<[^>]*>/gu,'').replace(/\s+/gu,' ').trim():'';
const cap=(value,n)=>[...clean(value)].slice(0,n).join('');
const money=value=>Number.isFinite(value)?`${value.toLocaleString('zh-CN')}¢`:'';
const events=rows=>(rows??[]).map(event=>[cap(event.name,80),cap(event.effect,220)].filter(Boolean).join('：')).filter(Boolean);

export function rentLine(rent){
  if(!rent)return '';
  if(rent.kind==='owned')return '店铺已经买下，不用交租。';
  const when=rent.days_until_due===0?'今天':rent.days_until_due===1?'明天':Number.isFinite(rent.due_day)?`第${rent.due_day}天`:'';
  return when?`${when}要交${rent.kind==='mortgage'?'房贷':'租金'} ${money(rent.amount)}。`:'';
}
export function journalSummary(journal){
  if(!journal)return [];
  const today=events(journal.today);
  return [today.length?`今日行情：${today.join('；')}。`:null,rentLine(journal.rent),
    journal.difficulty?.name?`玩法：${cap(journal.difficulty.name,50)}${journal.difficulty.endless?' · 无尽模式':''}。`:null,
    journal.traits?.length?`已激活特性：${journal.traits.map(p=>cap(p.name,40)).filter(Boolean).join('、')}。`:null,
    journal.reputation?.length?`声望：${journal.reputation.map(f=>`${cap(f.name,30)} ${f.value}${f.status?`（${cap(f.status,20)}）`:''}`).join('；')}。`:null,
    journal.black_market?.unlocked===false?'黑市尚未解锁。':journal.black_market?.unlocked===true?'黑市已解锁。':null].filter(Boolean);
}
export function journalView(detail){
  const lines=[];
  if(detail.topic==='overview')lines.push(...journalSummary(detail));
  else if(detail.topic==='calendar'){
    lines.push(`日历 · 第${detail.day}天`,rentLine(detail.rent));
    for(const day of detail.days??[]){const entries=events(day.events);if(entries.length)lines.push(`第${day.day}天${day.today?'（今天）':''}：${entries.join('；')}`);}
    if(Number.isFinite(detail.property_price))lines.push(`店铺当前售价：${money(detail.property_price)}。`);
  }else if(detail.topic==='status'){
    lines.push('已激活特性：');
    for(const trait of detail.traits??[])lines.push(`${cap(trait.name,80)}${trait.description?`：${cap(trait.description,1500)}`:''}`);
    if(!detail.traits?.length)lines.push('没有已激活特性。');
    lines.push(`现金 ${money(detail.cash)}；预估总资产 ${money(detail.estimated_total_assets)}。`);
  }else if(detail.topic==='reputation'){
    for(const faction of detail.factions??[]){
      lines.push(`${cap(faction.name,40)}：${faction.value}/200（${cap(faction.status,30)}）`);
      for(const perk of faction.perks??[])lines.push(`  ${perk.active?'已激活':'未激活'} · ${cap(perk.name,80)} · 声望门槛 ${perk.threshold}：${cap(perk.description,800)}`);
    }
  }else if(detail.topic==='difficulty')lines.push(`难度：${cap(detail.name,80)}${detail.endless?' · 无尽模式':''}`,cap(detail.description,500),...(detail.effects??[]).map(value=>cap(value,1000)));
  else if(detail.topic==='black_market'){
    if(detail.unlocked===false)return '这个存档的黑市尚未解锁。';
    lines.push('黑市已解锁。',cap(detail.note,300),...(detail.text??[]).map(value=>cap(value,1500)));
  }
  return lines.filter(Boolean).join('\n');
}
