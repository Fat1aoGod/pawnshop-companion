import fs from 'node:fs';
import path from 'node:path';
import {spawn} from 'node:child_process';
import {randomBytes} from 'node:crypto';

export function makeBridge(cfg){
  const root=cfg.projectRoot;let pairing,ensuring;
  function connection(){
    if(!pairing){
      const file=path.join(root,'runtime/connection.json');
      if(!fs.existsSync(file)){
        fs.mkdirSync(path.dirname(file),{recursive:true});
        try{fs.writeFileSync(file,JSON.stringify({port:17643,token:randomBytes(24).toString('hex')},null,2),{flag:'wx',mode:0o600});}
        catch(error){if(error.code!=='EEXIST')throw error;}
      }
      const raw=JSON.parse(fs.readFileSync(file,'utf8'));pairing={port:Number(raw.port),token:String(raw.token)};
    }
    return pairing;
  }
  async function health(signal){
    const c=connection();
    try{
      const response=await fetch(`http://127.0.0.1:${c.port}/health`,{headers:{authorization:`Bearer ${c.token}`},signal:AbortSignal.any([AbortSignal.timeout(1500),...(signal?[signal]:[])])});
      if(response.status===403)throw Object.assign(new Error('本机桥接配对信息不一致。'),{code:'pairing_mismatch'});
      return response.ok && (await response.json()).name==='pawnshop-companion';
    }catch(e){if(e.code==='pairing_mismatch'||signal?.aborted)throw e;return false;}
  }
  async function ensure(signal){
    if(ensuring)return ensuring;
    ensuring=(async()=>{
      pairing=null;if(await health(signal))return;signal?.throwIfAborted();
      const executable=cfg.nodePath||process.execPath;
      const child=spawn(executable,[path.join(root,'bridge/server.mjs')],{cwd:root,detached:true,windowsHide:true,stdio:'ignore',env:{...process.env,ELECTRON_RUN_AS_NODE:'1'}});
      let launchError;child.on('error',e=>{launchError=e;});child.unref();
      for(let i=0;i<20;i++){
        await new Promise(resolve=>setTimeout(resolve,200));signal?.throwIfAborted();
        if(launchError)throw launchError;if(await health(signal))return;
      }
      throw new Error('当铺搭子的本机桥接没有启动。');
    })();
    try{return await ensuring;}finally{ensuring=null;}
  }
  async function call(name,args={},signal){
    signal?.throwIfAborted();const c=connection();
    const response=await fetch(`http://127.0.0.1:${c.port}/agent/call`,{
      method:'POST',headers:{authorization:`Bearer ${c.token}`,'content-type':'application/json'},body:JSON.stringify({name,arguments:args}),
      signal:AbortSignal.any([AbortSignal.timeout(56000),...(signal?[signal]:[])])});
    const result=await response.json();
    if(!response.ok)throw Object.assign(new Error(result.message||result.error||`HTTP ${response.status}`),{code:result.error});
    return result;
  }
  return {ensure,call};
}
