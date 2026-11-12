import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {makeBridge} from './lib/bridge.mjs';
import {CompanionSession} from './lib/session.mjs';

export const name='dsh-pawnshop-companion';
export const inject=['tools','agents'];
const DIR=path.dirname(fileURLToPath(import.meta.url));
const TOOLS=JSON.parse(fs.readFileSync(path.join(DIR,'tools.json'),'utf8')).tools;
export function apply(ctx,config={}){
  let local={};try{local=JSON.parse(fs.readFileSync(path.join(DIR,'config.json'),'utf8'));}catch{}
  const cfg={projectRoot:path.resolve(DIR,'../..'),autoStart:true,waitTimeoutMs:45000,observationCooldownMs:30000,reconnectDelayMs:15000,
    displayName:'DeepSeek 搭子',avatarKey:'deepseek',...local,...config};
  const agentFor=()=>{
    const id=String(cfg.sessionId||'').trim();if(!id)return null;
    return ctx.agents.get(id)||ctx.agents.get('session-'+id.replace(/^session-/,''))||null;
  };
  const session=new CompanionSession({bridge:makeBridge(cfg),agentFor,config:cfg,
    stateFile:path.join(DIR,'binding-state.json'),log:message=>console.log('[pawnshop]',message)});
  let owner=null,disposers=[];
  const unregister=()=>{for(const dispose of disposers.splice(0))dispose();owner=null;};
  function registerForAgent(){
    const agent=agentFor();if(agent===owner)return;
    unregister();if(!agent)return;owner=agent;
    // The model waits on this host's inbox; it never starts another bridge poll.
    for(const tool of TOOLS)disposers.push(agent.ctx.tools.register({
      name:tool.name,
      description:tool.name==='companion_wait_events'?'陪着等一会儿，最多45秒。有新见闻或玩家消息就一起返回，继续这段对话；无动静时本段自然结束，由后台等下一次消息。不要循环空等。':tool.description,
      parameters:tool.name==='companion_wait_events'?{type:'object',properties:{timeout_ms:{type:'integer',minimum:0,maximum:45000}},additionalProperties:false}:tool.inputSchema,
      output:{schema:{type:'object',properties:{},additionalProperties:true},render:(_args,value)=>[{type:'text',text:JSON.stringify(value,null,2)}]},
      timeoutMs:70000,
      async execute(args={},exec={}){
        if(exec.agent!==agent || agent!==owner || agent!==agentFor())return {error:'wrong_session',message:'这局工具属于选定的陪玩会话。'};
        try{
          if(tool.name==='companion_connect')return await session.start(args,exec.signal);
          if(tool.name==='companion_disconnect')return await session.stop(args.binding_id);
          if(tool.name==='companion_wait_events'){
            const result=await session.waitForEvents(args,exec.signal);
            if(result.status==='idle')exec.concludeTurn?.();
            return result;
          }
          return await session.call(tool.name,args,exec.signal);
        }catch(e){return {error:e.code||'bridge_unavailable',message:e.message};}
      },
    }));
    disposers.push(agent.ctx.on('agent/inbox/inserted',()=>session.interruptWait()));
  }
  const flush=()=>{void session.flush().catch(e=>console.log('[pawnshop]',e.message));};
  ctx.on('agent/created',()=>{registerForAgent();flush();});
  ctx.on('agent/disposed',()=>{registerForAgent();});
  ctx.on('agent/status',flush);
  ctx.effect(()=>{
    registerForAgent();session.resume();
    return async()=>{unregister();await session.dispose();};
  });
}
