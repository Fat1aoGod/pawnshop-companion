// Local diagnostic/first-chat fallback. It calls the same bridge as MCP;
// it never starts another model or reads another chat.
import {callBridge} from './client.mjs';
let raw='';for await(const chunk of process.stdin)raw+=chunk;
try{console.log(JSON.stringify(await callBridge(process.argv[2],raw.trim()?JSON.parse(raw):{}),null,2));}
catch(error){console.error(JSON.stringify({error:error.code||'bridge_unavailable',message:error.message}));process.exitCode=1;}
