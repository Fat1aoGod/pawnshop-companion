import http from 'node:http';
import {randomBytes, randomUUID, timingSafeEqual} from 'node:crypto';
import {readFileSync, writeFileSync, mkdirSync, existsSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import path from 'node:path';
import {TextView} from '../prototype/text-view.mjs';
import {InventoryCatalog} from './inventory-catalog.mjs';
import {journalView} from '../prototype/journal-view.mjs';

export const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export function localConfig() {
  const file = process.env.PAWNSHOP_COMPANION_CONFIG || path.join(root, 'runtime', 'connection.json');
  if (!existsSync(file)) {
    mkdirSync(path.dirname(file), {recursive:true});
    writeFileSync(file, JSON.stringify({port:17643, token:randomBytes(24).toString('hex')}, null, 2), {mode:0o600,flag:'wx'});
  }
  return JSON.parse(readFileSync(file, 'utf8'));
}
const problem = (code, message) => Object.assign(new Error(message), {code});
const limited = (text, n=1000) => String(text ?? '').slice(0,n);

export function createBridge({token, port=0, host='127.0.0.1', now=Date.now}={}) {
  if (!token) throw new Error('Local pairing token required');
  const games=new Map();
  const changed=new Set();
  const wake=()=>{for(const callback of [...changed]) callback();};
  const fresh=game=>game?.active && now()-game.seen<12000;
  function publish(game, packets) {
    for(const packet of packets) game.events.push({...packet,cursor:++game.cursor,event_id:randomUUID()});
    game.events=game.events.slice(-192); wake();
  }
  function resolve(args) {
    const game=[...games.values()].find(g=>g.binding?.id===args.binding_id);
    if(!game) throw problem('not_connected','这次陪玩已断开。');
    if(!fresh(game)) throw problem('offline','游戏暂时没有连着，等玩家回来。');
    return game;
  }
  async function waitUntil(check, timeout) {
    const immediate=check(); if(immediate) return immediate;
    return new Promise(resolve=>{
      let timer;
      const finish=value=>{clearTimeout(timer); changed.delete(poll);resolve(value);};
      const poll=()=>{const value=check();if(value)finish(value);};
      changed.add(poll); timer=setTimeout(()=>finish(null),timeout);
    });
  }
  function cancel(game, status) {
    for(const command of game.commands.values()) if(!game.receipts.has(command.id))
      game.receipts.set(command.id,{command_id:command.id,action:command.action,status,summary:'这次操作没有继续。'});
    game.commands.clear(); wake();
  }
  async function command(game,args,action) {
    if(args.context_id!==game.context) throw problem('stale_context','存档或场景已切换，先重新看一眼。');
    const id=args.action_id || randomUUID();
    if(!/^[\w-]{1,100}$/u.test(id)) throw problem('invalid_request','操作编号无效。');
    const request={...args}; delete request.binding_id; delete request.action_id;
    const fingerprint=JSON.stringify({action,request});
    const previous=game.requests.get(id);
    if(previous && previous!==fingerprint) throw problem('id_conflict','同一操作编号不能用于不同动作。');
    if(game.receipts.has(id)) return game.receipts.get(id);
    if(!game.commands.has(id)) {
      if(game.commands.size>=24) throw problem('busy','手边的事情先等一下。');
      game.requests.set(id,fingerprint);
      const replyTo=args.in_reply_to && game.events.find(e=>e.event_id===args.in_reply_to);
      game.commands.set(id,{...request,id,action,binding_id:game.binding.id,
        visit_id:replyTo?.visit_id || null,actor:game.binding.name,expires_at:now()+15000});
    }
    const receipt=await waitUntil(()=>game.receipts.get(id),Math.min(args.wait_ms??7000,8000));
    return receipt || {command_id:id,action,status:'queued',summary:'还在等游戏收到，尚未执行。'};
  }
  async function call(name,args={}) {
    if(name==='companion_list_games') return {games:[...games.values()].filter(fresh).map(g=>({game_instance_id:g.id,context_id:g.context,player:g.player,bound_to:g.binding?.name || null}))};
    if(name==='companion_connect') {
      const game=games.get(args.game_instance_id);
      if(!fresh(game)) throw problem('offline','还没找到正在陪玩的游戏。请在游戏里开启「当铺搭子」。');
      if(game.binding) throw problem('already_connected','店里已经有位伙伴，先由游戏面板断开。');
      game.binding={id:randomUUID(),name:limited(args.display_name||'伙伴',30),avatar:args.avatar_key||(/deepseek/iu.test(args.display_name||'')?'deepseek':'gpt')};
      const waiting_messages=game.events.filter(e=>e.kind==='player_message' && e.cursor>(game.lastBindingCursor||0)).slice(-16);
      game.lastBindingCursor=game.cursor;
      return {binding_id:game.binding.id,context_id:game.context,cursor:game.cursor,view:game.view.read(game.snapshot),scope:game.scope,waiting_messages};
    }
    const game=resolve(args);
    if(name==='companion_disconnect') {cancel(game,'disconnected');game.binding=null;game.catalog=null;return {status:'disconnected'};}
    if(name==='companion_read_state') return {context_id:game.context,visit_id:game.snapshot.customer?.visit_id,
      cursor:game.cursor,view:game.view.read(game.snapshot,{focus:args.focus,detailed:args.detailed}),scope:game.scope};
    if(name==='companion_wait_events') {
      const cursor=Number(args.after_cursor)||0;
      await waitUntil(()=>game.events.some(e=>e.cursor>cursor)||!fresh(game)||!game.binding,Math.max(0,Math.min(args.timeout_ms??45000,50000)));
      if(!game.binding) return {status:'disconnected',events:[],cursor:game.cursor};
      const events=game.events.filter(e=>e.cursor>cursor);
      return {status:fresh(game)?'connected':'offline',context_id:game.context,events,cursor:game.cursor,
        missed_events:cursor<game.cursor && cursor<(game.events[0]?.cursor??game.cursor)-1};
    }
    if(name==='companion_receipt') return game.receipts.get(args.action_id) || {status:game.commands.has(args.action_id)?'queued':'unknown'};
    const catalogReads={companion_list_containers:'list',companion_find_item:'find',companion_read_container:'read'};
    if(catalogReads[name]) {
      if(game.scope.inspect_containers!==true)throw problem('blocked','仓库和箱子还没有交给伙伴查看。');
      if(!game.catalog)throw problem('catalog_needed','先用 companion_inspect_containers 看一遍仓库和箱子。');
      if(args.catalog_id && args.catalog_id!==game.catalog.id)throw problem('stale_catalog','清单已经换过了，先看最新那份。');
      const stale=game.catalog.changed || game.catalog.revision!==game.scope.inventory_revision;
      return {...game.catalog[catalogReads[name]](args),stale,
        ...(stale?{note:'这份清单之后库存可能动过；需要准确位置时重新看一遍。'}:{})};
    }
    const actions={companion_say:'say',companion_sort_storage:'sort_storage',companion_undo_storage_sort:'undo_storage_sort',
      companion_inspect_storage:'inspect_storage',companion_inspect_power:'inspect_power',companion_swap_battery:'swap_battery',
      companion_inspect_containers:'inspect_containers',companion_item_options:'item_options',companion_item_action:'item_action',
      companion_machine_controls:'machine_controls',companion_machine_control:'machine_control',companion_move_item:'move_item',
      companion_use_item_on:'use_item_on',companion_read_confirmation:'read_confirmation',companion_answer_confirmation:'answer_confirmation',
      companion_read_journal:'read_journal',companion_open_journal:'open_journal'};
    if(!actions[name]) throw problem('unknown_tool','没有这个陪玩动作。');
    if(name==='companion_read_journal' && !['overview','calendar','status','reputation','difficulty','black_market'].includes(args.topic??'overview'))throw problem('invalid_request','没有这个店铺信息页。');
    if(name==='companion_open_journal' && !['calendar','status','reputation','black_market'].includes(args.topic))throw problem('invalid_request','没有这个可打开的信息页。');
    if(name==='companion_say' && (!args.text?.trim() || args.text.length>1000)) throw problem('invalid_request','发言请控制在一千字以内。');
    return command(game,args,actions[name]);
  }
  async function sync(body) {
    if(body.protocol_version!=='0.1' || !body.game_instance_id || !body.context_id) throw problem('protocol_mismatch','Unsupported game bridge protocol');
    let game=games.get(body.game_instance_id);
    if(!game){
      if(games.size>=8) throw problem('busy','Too many game instances');
      game={id:body.game_instance_id,context:body.context_id,seen:now(),active:false,view:new TextView(),snapshot:{},scope:{},player:'',
        events:[],cursor:0,binding:null,commands:new Map(),receipts:new Map(),requests:new Map()};
      games.set(game.id,game);
    }
    if(game.context!==body.context_id){cancel(game,'stale_context');game.context=body.context_id;game.requests.clear();game.receipts.clear();game.events=[];game.snapshot={};game.catalog=null;game.view=new TextView();}
    game.seen=now();game.active=body.active===true;
    if(!game.active || body.disconnect===true){cancel(game,'disconnected');game.binding=null;game.catalog=null;}
    if(body.snapshot){
      if(body.snapshot.context_id!==body.context_id) throw problem('stale_context','Snapshot context mismatch');
      game.snapshot=body.snapshot;game.player=limited(body.snapshot.player_name,30);
    }
    if(body.scope){game.scope=body.scope;if(game.scope.inspect_containers!==true)game.catalog=null;}
    const events=Array.isArray(body.events)?body.events.slice(0,32):[];
    for(const ack of (body.receipts||[]).slice(0,32)) {
      const pending=game.commands.get(ack.command_id);
      if(!pending || game.receipts.has(ack.command_id))continue;
      const receipt={command_id:pending.id,action:pending.action,status:limited(ack.status,40),summary:limited(ack.summary,1000)};
      if(pending.action==='inspect_containers' && ack.status==='observed' && ack.detail?.kind==='container_catalog'){
        if(ack.detail.context_id!==game.context || game.scope.inspect_containers!==true) {
          receipt.status='unavailable';receipt.summary='这份清单已经过时，没有继续使用。';
        } else {
          game.catalog=new InventoryCatalog(ack.detail);receipt.detail=game.catalog.overview();receipt.summary=receipt.detail.summary;
        }
      }else if(ack.detail){receipt.detail=ack.detail;if(pending.action==='read_journal')receipt.view=journalView(ack.detail);}
      game.receipts.set(pending.id,receipt);game.commands.delete(pending.id);
      events.push({id:pending.id,type:'action_result',actor:pending.actor,receipt});
    }
    // Keep acknowledgements bounded without evicting an unacknowledged command.
    while(game.receipts.size>256){const id=game.receipts.keys().next().value;game.receipts.delete(id);game.requests.delete(id);}
    if(body.snapshot || events.length) publish(game,game.view.observe(game.snapshot,events));
    for(const [id,pending] of game.commands) if(pending.expires_at<now()){
      game.receipts.set(id,{command_id:id,action:pending.action,status:'expired',summary:'游戏没有及时接到，这次没有执行。'});game.commands.delete(id);
    }
    wake();
    return {protocol_version:'0.1',binding_id:game.binding?.id || null,companion_name:game.binding?.name || null,companion_avatar:game.binding?.avatar || null,
      commands:game.active?[...game.commands.values()].slice(0,4):[]};
  }
  function authorized(req) {
    if(req.headers.origin) return false;
    const supplied=Buffer.from(req.headers.authorization||'');const expected=Buffer.from(`Bearer ${token}`);
    return supplied.length===expected.length && timingSafeEqual(supplied,expected);
  }
  const server=http.createServer(async(req,res)=>{
    const send=(status,value)=>{if(!res.destroyed){res.writeHead(status,{'Content-Type':'application/json; charset=utf-8','Cache-Control':'no-store'});res.end(JSON.stringify(value));}};
    if(!authorized(req)) return send(403,{error:'local_pairing_required'});
    try {
      if(req.method==='GET' && req.url==='/health') return send(200,{name:'pawnshop-companion',version:'0.7.0'});
      if(req.method!=='POST') return send(404,{error:'not_found'});
      const chunks=[];let size=0;const limit=req.url==='/game/sync'?8388608:65536;
      for await(const chunk of req){size+=chunk.length;if(size>limit)throw problem('too_large','Request too large');chunks.push(chunk);}
      const body=JSON.parse(Buffer.concat(chunks).toString('utf8'));
      if(req.url==='/game/sync') return send(200,await sync(body));
      if(req.url==='/agent/call') return send(200,await call(body.name,body.arguments));
      send(404,{error:'not_found'});
    }catch(error){send(400,{error:error.code||'invalid_request',message:error.message});}
  });
  server.requestTimeout=60000;
  return {server,call,sync,listen:()=>new Promise(resolve=>server.listen(port,host,()=>resolve(server.address()))),
    close:()=>{for(const g of games.values()){g.active=false;g.binding=null;}wake();server.closeAllConnections();return new Promise(resolve=>server.close(resolve));}};
}
if(process.argv[1] && path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
  const config=localConfig();const bridge=createBridge(config);
  const address=await bridge.listen();console.log(`当铺搭子已就绪 · 本机端口 ${address.port}`);
  for(const signal of ['SIGINT','SIGTERM'])process.on(signal,async()=>{await bridge.close();process.exit(0);});
}
