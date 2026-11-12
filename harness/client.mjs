import {localConfig} from '../bridge/server.mjs';
export async function callBridge(name,args={},config=localConfig()) {
  const response=await fetch(`http://127.0.0.1:${config.port}/agent/call`,{
    method:'POST',headers:{authorization:`Bearer ${config.token}`,'content-type':'application/json'},
    body:JSON.stringify({name,arguments:args}),signal:AbortSignal.timeout(56000)
  });
  const result=await response.json();
  if(!response.ok)throw Object.assign(new Error(result.message||result.error),{code:result.error});
  return result;
}
