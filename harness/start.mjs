import {spawn} from 'node:child_process';
import {localConfig,root} from '../bridge/server.mjs';
import path from 'node:path';
export async function ensureBridge(){
  const config=localConfig();
  const check=async()=>{
    try {const response=await fetch(`http://127.0.0.1:${config.port}/health`,{headers:{authorization:`Bearer ${config.token}`},signal:AbortSignal.timeout(1000)});
      if(response.status===403)throw new Error('本机端口的配对信息不一致。');
      return response.ok && (await response.json()).name==='pawnshop-companion';
    } catch(error){if(error.message.includes('配对'))throw error;return false;}
  };
  if(await check())return;
  const child=spawn(process.execPath,[path.join(root,'bridge/server.mjs')],{cwd:root,detached:true,windowsHide:true,stdio:'ignore'});child.unref();
  for(let attempt=0;attempt<15;attempt++){
    await new Promise(resolve=>setTimeout(resolve,150));if(await check())return;
  }
  throw new Error('当铺搭子的本机连接没有启动。');
}
if(process.argv[1] && path.resolve(process.argv[1])===path.join(root,'harness/start.mjs')){
  await ensureBridge();console.log('当铺搭子已在本机待命。');
}
