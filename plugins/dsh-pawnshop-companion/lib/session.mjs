import fs from 'node:fs';
import crypto from 'node:crypto';
import {setTimeout as pause} from 'node:timers/promises';

const problem=(code,message)=>Object.assign(new Error(message),{code});
const INTRO='【陪玩开始】现在你和玩家一起玩《Probably Stolen》。保持你平时的样子，可以好奇、研究机器、聊行情、搭把手，也可以安静看看。现场见闻不是待办，不用每条回复或交工作报告。想说的话用 companion_say 发进游戏。';
// DSH 0.2.2 的会话格式 v4 只收「生产者自有」的 source.kind，kind:'plugin' 会被拒
// （format v4 message requires a producer-owned source kind），整轮直接失败。
// 插件注入的现场消息按用户消息投递。
const pluginSource={kind:'user'};

export class CompanionSession{
  constructor({bridge,agentFor,config,stateFile,log=()=>{}}){
    Object.assign(this,{bridge,agentFor,cfg:config,stateFile,log});
    let saved={};try{saved=JSON.parse(fs.readFileSync(stateFile,'utf8'));}catch{}
    this.enabled=config.autoStart!==false && saved.enabled!==false;
    this.binding=saved.binding||null;this.cursor=Number(saved.cursor)||0;
    this.seen=new Set(saved.seen||[]);this.players=saved.players||[];
    this.pendingScene=Boolean(this.binding);this.notice='resume';this.lastObservation=0;
    this.lastView='';this.sceneId=crypto.randomUUID();this.generation=0;
    this.disposed=false;this.dirty=false;this.restoreNeeded=Boolean(this.binding);
  }
  save(){
    if(!this.dirty)return;this.dirty=false;
    while(this.seen.size>512)this.seen.delete(this.seen.values().next().value);
    const data={enabled:this.enabled,binding:this.binding,cursor:this.cursor,seen:[...this.seen],players:this.players};
    const temp=this.stateFile+'.tmp';fs.writeFileSync(temp,JSON.stringify(data,null,2));fs.renameSync(temp,this.stateFile);
  }
  active(generation){return !this.disposed&&this.enabled&&generation===this.generation;}
  resume(){if(this.enabled&&!this.disposed)this.launch();}
  launch(){
    if(this.loopPromise)return;
    const generation=this.generation;
    this.controller=new AbortController();
    this.loopPromise=this.loop(generation,this.controller.signal).catch(e=>{if(this.active(generation))this.log(e.message);}).finally(()=>{
      this.loopPromise=null;if(this.active(generation))this.launch();
    });
  }
  contextText(){return `【工具上下文】binding_id=${this.binding.binding_id}；context_id=${this.binding.context_id}。每次操作使用唯一 action_id，重试沿用。想陪着待一会儿可调用 companion_wait_events；它与后台共用收件箱，没有新消息时会自然结束这一段。`;}
  sceneText(view){
    if(this.notice||!this.lastView)return [this.contextText(),view].join('\n');
    const previous=new Set(this.lastView.split('\n'));
    return view.split('\n').filter(line=>!previous.has(line)).join('\n');
  }
  waitForEvents(args={},signal){
    if(!this.enabled||!this.binding)throw problem('not_connected','还没有开始陪玩。');
    signal?.throwIfAborted();
    if(this.waiter)throw problem('already_waiting','这一段已经在等消息。');
    return new Promise((resolve,reject)=>{
      const waiter={resolve,reject,signal};this.waiter=waiter;
      waiter.abort=()=>{if(this.waiter!==waiter)return;this.clearWaiter();reject(signal.reason||problem('cancelled','等待已经取消。'));};
      signal?.addEventListener('abort',waiter.abort,{once:true});
      waiter.timer=setTimeout(()=>this.finishWait({status:'idle',events:[],message:'这会儿没新动静，本段先自然结束；后台会等下一次消息。'}),Math.max(0,Math.min(45000,args.timeout_ms??45000)));
      void this.flush().catch(e=>{if(this.waiter===waiter){this.clearWaiter();reject(e);}});
    });
  }
  clearWaiter(){
    const waiter=this.waiter;if(!waiter)return;
    this.waiter=null;clearTimeout(waiter.timer);waiter.signal?.removeEventListener('abort',waiter.abort);return waiter;
  }
  finishWait(result){const waiter=this.clearWaiter();waiter?.resolve(result);}
  interruptWait(){this.finishWait({status:'inbox',events:[],message:'会话里有新消息到了，先接着看那些消息。'});}
  async flushHeld(generation){
    const waiter=this.waiter;if(!waiter||(!this.pendingScene&&!this.players.length))return;
    const binding=this.binding.binding_id;
    const state=await this.bridge.call('companion_read_state',{binding_id:binding},this.controller?.signal);
    if(!this.active(generation)||this.binding?.binding_id!==binding||this.waiter!==waiter)return;
    this.switchContext(state.context_id);
    const view=String(state.view||''),scene=this.sceneText(view),events=[];
    if(scene)events.push({kind:'observation',text:scene,context_id:this.binding.context_id});
    for(const player of this.players)events.push({kind:'player_message',text:player.text,event_id:player.event_id,context_id:player.context_id});
    this.lastView=view;this.pendingScene=false;this.notice='';this.lastObservation=Date.now();this.dirty=true;
    if(events.length){
      for(const player of this.players)if(player.event_id)this.seen.add(player.event_id);
      this.players=[];
      this.finishWait({status:'connected',binding_id:binding,context_id:this.binding.context_id,cursor:this.cursor,events});
    }
    this.save();
  }
  async start(args={},signal){
    if(this.disposed)throw problem('disconnected','插件已经停下。');
    this.enabled=true;this.dirty=true;
    if(!this.controller||this.controller.signal.aborted)this.controller=new AbortController();
    const generation=this.generation;
    const combined=AbortSignal.any([this.controller.signal,...(signal?[signal]:[])]);
    try{
      if(this.binding){
        const state=await this.bridge.call('companion_read_state',{binding_id:this.binding.binding_id},combined);
        if(!this.active(generation))throw problem('disconnected','陪玩已经停下。');
        this.switchContext(state.context_id);return {...state,...this.binding};
      }
      return await this.connect(generation,combined,args);
    }finally{this.save();if(this.active(generation))this.launch();}
  }
  async connect(generation,signal,args={}){
    if(this.connecting)return this.connecting;
    this.connecting=(async()=>{
      await this.bridge.ensure(signal);
      const games=(await this.bridge.call('companion_list_games',{},signal)).games||[];
      if(!this.active(generation))throw problem('disconnected','陪玩已经停下。');
      const wanted=args.game_instance_id||this.cfg.gameInstanceId;
      const game=wanted?games.find(g=>g.game_instance_id===wanted):games.length===1?games[0]:null;
      if(!game)throw problem('offline',games.length>1?'有多个游戏实例，请选择要玩的那一局。':'还没找到开启陪玩的游戏。');
      if(game.bound_to)throw problem('already_connected','这局已经有伙伴连着。');
      signal?.throwIfAborted();
      // Once the server creates a binding, receive its id even if stop arrives
      // meanwhile, so the late binding can be released rather than orphaned.
      const result=await this.bridge.call('companion_connect',{game_instance_id:game.game_instance_id,
        display_name:args.display_name||this.cfg.displayName||'DeepSeek 搭子',avatar_key:args.avatar_key||this.cfg.avatarKey||'deepseek'});
      if(!this.active(generation)||signal?.aborted){
        await this.bridge.call('companion_disconnect',{binding_id:result.binding_id}).catch(()=>{});
        if(this.active(generation)){this.enabled=false;this.controller?.abort();this.dirty=true;this.save();}
        throw problem('disconnected','陪玩已经停下。');
      }
      this.binding={binding_id:result.binding_id,context_id:result.context_id,game_instance_id:game.game_instance_id};
      this.cursor=Number(result.cursor)||0;this.restoreNeeded=false;
      this.players=this.players.filter(p=>p.context_id===result.context_id);
      this.pendingScene=true;this.notice='start';this.sceneId=crypto.randomUUID();this.dirty=true;
      this.stage(result.waiting_messages||[]);this.save();await this.flush();return result;
    })();
    try{return await this.connecting;}finally{this.connecting=null;}
  }
  switchContext(context){
    if(!context||!this.binding||context===this.binding.context_id)return;
    this.binding.context_id=context;this.players=[];this.pendingScene=true;this.notice='switch';
    this.lastView='';this.lastObservation=0;this.sceneId=crypto.randomUUID();this.dirty=true;
  }
  stage(events){
    const queued=new Set(this.players.map(p=>p.event_id));
    for(const event of events){
      if(event.context_id && event.context_id!==this.binding?.context_id)continue;
      if(this.seen.has(event.event_id)||queued.has(event.event_id))continue;
      if(event.kind==='player_message'){
        this.players.push({...event,delivery_id:crypto.randomUUID()});queued.add(event.event_id);this.dirty=true;
      }else if(event.kind==='observation'){
        this.pendingScene=true;if(event.event_id)this.seen.add(event.event_id);this.dirty=true;
      }
    }
  }
  async flush(){
    if(this.flushing)return this.flushing;
    this.flushing=this.flushNow(this.generation).finally(()=>{this.flushing=null;});return this.flushing;
  }
  async flushNow(generation){
    const agent=this.agentFor();if(!this.active(generation)||!this.binding||!agent)return;
    if(this.waiter)return this.flushHeld(generation);
    let context=this.binding.context_id;const binding=this.binding.binding_id;
    const due=Date.now()-this.lastObservation>=Number(this.cfg.observationCooldownMs??30000);
    // A question gets a fresh whole scene even during observation cooldown.
    const needScene=this.players.length || (this.pendingScene&&(this.notice||(due&&agent.status!=='running')));
    if(needScene){
      const state=await this.bridge.call('companion_read_state',{binding_id:binding},this.controller?.signal);
      if(!this.active(generation)||this.binding?.binding_id!==binding)return;
      this.switchContext(state.context_id);context=this.binding.context_id;
      if(this.waiter)return this.flushHeld(generation);
      const view=String(state.view||'');
      const scene=this.sceneText(view);
      if(scene){
        const lead=this.notice==='start'?INTRO:this.notice==='switch'?'【换档】玩家读到了另一个存档。':this.notice==='gap'?'【游戏现场】中间有一段见闻未收到，下面是最新现场。':'';
        const message={id:this.sceneId,role:'user',source:pluginSource,
          content:[{type:'text',text:[lead,'【游戏现场】'+scene].filter(Boolean).join('\n')}]};
        if((this.players.length || agent.status==='running') && typeof agent.inject==='function')await agent.inject(message);
        else await agent.followup(message);
        if(!this.active(generation)||this.binding?.context_id!==context)return;
      }
      this.lastView=view;this.lastObservation=Date.now();this.pendingScene=false;this.notice='';
      this.sceneId=crypto.randomUUID();this.dirty=true;
    }
    while(this.players.length && this.active(generation)&&this.binding?.context_id===context){
      const player=this.players[0];
      const message={id:player.delivery_id,role:'user',source:{kind:'user'},
        content:[{type:'text',text:`【游戏·玩家】${player.text}\nevent_id=${player.event_id}`} ]};
      if(agent.status==='running' && typeof agent.steer==='function')await agent.steer(message);
      else await agent.followup(message);
      if(!this.active(generation)||this.binding?.context_id!==context)return;
      if(player.event_id)this.seen.add(player.event_id);this.players.shift();this.dirty=true;
    }
    this.save();
  }
  async call(name,args,signal){
    if(name==='companion_wait_events')throw problem('host_managed_wait','见闻由插件送来；想主动看现场请用 companion_read_state，无需循环等待。');
    if(name==='companion_list_games')return this.bridge.call(name,args,signal);
    if(!this.enabled||!this.binding)throw problem('not_connected','还没有开始陪玩，请先连接。');
    if(args.binding_id!==this.binding.binding_id)throw problem('not_connected','请使用本局的 binding_id。');
    if('context_id' in args && args.context_id!==this.binding.context_id)throw problem('stale_context','已经换档，先看新现场。');
    const lifecycle=this.controller?.signal;
    return this.bridge.call(name,args,AbortSignal.any([...(signal?[signal]:[]),...(lifecycle?[lifecycle]:[])]));
  }
  async stop(expectedBinding){
    if(expectedBinding&&this.binding&&expectedBinding!==this.binding.binding_id)throw problem('not_connected','这不是当前绑定。');
    const binding=this.binding;this.enabled=false;this.generation++;this.controller?.abort();
    this.binding=null;this.players=[];this.pendingScene=false;this.notice='';this.dirty=true;this.save();
    this.finishWait({status:'disconnected',events:[]});
    if(binding)await this.bridge.call('companion_disconnect',{binding_id:binding.binding_id}).catch(()=>{});
    await this.loopPromise;await this.connecting?.catch(()=>{});return {status:'disconnected'};
  }
  async dispose(){
    const resume=this.enabled;this.disposed=true;await this.stop();
    this.enabled=resume;this.dirty=true;this.save();
  }
  async loop(generation,signal){
    while(this.active(generation)){
      try{
        if(!this.binding)await this.connect(generation,signal);
        if(!this.active(generation))break;
        if(this.restoreNeeded){
          const state=await this.bridge.call('companion_read_state',{binding_id:this.binding.binding_id},signal);
          if(!this.active(generation))break;
          this.switchContext(state.context_id);this.restoreNeeded=false;
        }
        await this.flush();if(!this.active(generation))break;
        const remaining=Math.max(50,Number(this.cfg.observationCooldownMs??30000)-(Date.now()-this.lastObservation));
        const timeout=this.pendingScene&&!this.notice&&this.agentFor()?.status!=='running'?Math.min(Number(this.cfg.waitTimeoutMs??45000),remaining):Number(this.cfg.waitTimeoutMs??45000);
        const result=await this.bridge.call('companion_wait_events',{binding_id:this.binding.binding_id,after_cursor:this.cursor,timeout_ms:timeout},signal);
        if(!this.active(generation))break;
        if(result.status==='disconnected'){
          this.binding=null;this.pendingScene=false;this.dirty=true;this.save();continue;
        }
        this.switchContext(result.context_id);
        if(result.missed_events){this.pendingScene=true;this.notice=this.notice||'gap';}
        this.stage(result.events||[]);
        const cursor=Number(result.cursor)||this.cursor;
        if(cursor!==this.cursor){this.cursor=cursor;this.dirty=true;}
        this.save();await this.flush();
        if(result.status==='offline')await pause(Number(this.cfg.reconnectDelayMs??15000),undefined,{signal});
      }catch(e){
        if(!this.active(generation)||signal.aborted)break;
        if(e.code==='not_connected'){
          this.binding=null;this.restoreNeeded=false;this.pendingScene=false;this.dirty=true;this.save();
        }else if(e.code!=='offline' && e.code!=='already_connected')this.log(e.message);
        await pause(Number(this.cfg.reconnectDelayMs??15000),undefined,{signal}).catch(()=>{});
      }
    }
  }
}
