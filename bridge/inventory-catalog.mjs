const text=value=>typeof value==='string'?value.replace(/\s+/gu,' ').trim().slice(0,120):'';
const fail=(code,message)=>{throw Object.assign(new Error(message),{code});};

// One on-demand inventory snapshot, kept locally. Model responses use small
// summaries and searchable pages rather than copying the whole warehouse.
export class InventoryCatalog {
  constructor(raw) {
    this.id=text(raw.catalog_id);this.context=raw.context_id;this.revision=raw.inventory_revision;
    this.captured_at=new Date().toISOString();this.truncated=raw.truncated===true;
    this.changed=raw.changed_during_scan===true;this.unread=Math.max(0,Number(raw.unread_count)||0);
    this.roots=new Map((raw.roots||[]).map(root=>[text(root.ref),text(root.name)]));
    this.items=new Map();this.children=new Map();this.groups=new Map();
    for(const record of (raw.items||[]).slice(0,12000)) {
      const ref=text(record.ref),name=text(record.name);if(!ref || !name || this.items.has(ref))continue;
      const item={ref,name,parent_ref:text(record.parent_ref),quantity:Math.max(0,Number(record.quantity)||0),
        is_container:record.is_container===true,has_contents:record.has_contents===true,
        access:['readable','locked','sealed','unavailable'].includes(record.access)?record.access:'unavailable'};
      if(Number.isInteger(record.grid_x)&&Number.isInteger(record.grid_y)){item.grid_x=record.grid_x;item.grid_y=record.grid_y;}
      this.items.set(ref,item);
      if(!this.children.has(item.parent_ref))this.children.set(item.parent_ref,[]);
      this.children.get(item.parent_ref).push(item);
      if(!item.is_container)this.groups.set(name,(this.groups.get(name)||0)+item.quantity);
    }
    this.containers=[...this.items.values()].filter(item=>item.is_container);
  }
  path(ref) {
    const parts=[],seen=new Set();let current=ref;
    while(current && !seen.has(current) && parts.length<80){
      seen.add(current);
      if(this.roots.has(current)){parts.unshift(this.roots.get(current));break;}
      const item=this.items.get(current);if(!item)break;
      const position=item.grid_x!==undefined?`，位置 ${item.grid_x},${item.grid_y}`:'';
      parts.unshift(`${item.name}〔${item.ref.replace(/^item-/u,'#')}${position}〕`);
      current=item.parent_ref;
    }
    return parts.join(' → ').slice(0,1400);
  }
  overview() {
    const readable=this.containers.filter(item=>item.access==='readable');
    const empty=readable.filter(item=>(this.children.get(item.ref)||[]).length===0);
    const totalUnits=[...this.groups.values()].reduce((sum,count)=>sum+count,0);
    const preview=[...this.groups].sort((a,b)=>b[1]-a[1]).slice(0,12).map(([name,quantity])=>({name,quantity}));
    const qualifier=this.truncated?'本次清单尚未读完。':this.changed?'查看途中库存发生过变化，位置需要时再确认。':this.unread?'有锁着、未拆封或暂时读不到的内容已跳过。':'箱子保持原来的开关状态。';
    return {catalog_id:this.id,captured_at:this.captured_at,inventory_revision:this.revision,
      roots:[...this.roots].map(([ref,name])=>({ref,name})),item_stacks:this.items.size,item_units_excluding_containers:totalUnits,
      container_count:this.containers.length,readable_containers:readable.length,empty_containers:empty.length,
      unread_count:this.unread,truncated:this.truncated,changed_during_scan:this.changed,
      summary:`看过 ${readable.length} 个可读箱子，其中 ${empty.length} 个空箱；记录了 ${this.items.size} 处物品。${qualifier}`,
      preview};
  }
  describe(item){return {...item,path:this.path(item.ref),direct_items:(this.children.get(item.ref)||[]).length};}
  list({offset=0,limit=25}={}) {
    const start=Math.max(0,Math.trunc(offset)||0),size=Math.min(60,Math.max(1,Math.trunc(limit)||25));
    return {catalog_id:this.id,total:this.containers.length,offset:start,
      containers:this.containers.slice(start,start+size).map(item=>this.describe(item)),
      next_offset:start+size<this.containers.length?start+size:null};
  }
  find({query,offset=0,limit=25}={}) {
    const key=text(query).normalize('NFKC').toLocaleLowerCase();
    if(!key)fail('invalid_request','先说想找什么物品。');
    const matches=[...this.items.values()].filter(item=>item.name.normalize('NFKC').toLocaleLowerCase().includes(key));
    const start=Math.max(0,Math.trunc(offset)||0),size=Math.min(60,Math.max(1,Math.trunc(limit)||25));
    return {catalog_id:this.id,query,total_locations:matches.length,total_quantity:matches.reduce((sum,item)=>sum+item.quantity,0),
      matches:matches.slice(start,start+size).map(item=>this.describe(item)),next_offset:start+size<matches.length?start+size:null};
  }
  read({container_ref,offset=0,limit=40}={}) {
    const item=this.items.get(container_ref);
    if(!this.roots.has(container_ref) && !item)fail('unavailable','这份清单里找不到这个箱子。');
    if(item && (!item.has_contents || item.access!=='readable'))return {catalog_id:this.id,container:this.describe(item),status:item.access==='readable'?'not_a_container':item.access,items:[]};
    const entries=this.children.get(container_ref)||[];
    const start=Math.max(0,Math.trunc(offset)||0),size=Math.min(60,Math.max(1,Math.trunc(limit)||40));
    return {catalog_id:this.id,container_ref,path:this.path(container_ref),total:entries.length,
      items:entries.slice(start,start+size).map(entry=>this.describe(entry)),next_offset:start+size<entries.length?start+size:null};
  }
}
