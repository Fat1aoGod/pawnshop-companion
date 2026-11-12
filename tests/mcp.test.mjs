import test from 'node:test';
import assert from 'node:assert/strict';
import path from 'node:path';
import {Client} from '@modelcontextprotocol/client';
import {StdioClientTransport} from '@modelcontextprotocol/client/stdio';
import {root,createBridge} from '../bridge/server.mjs';
import {mkdtemp,writeFile,rm} from 'node:fs/promises';
import os from 'node:os';

test('official MCP client negotiates stdio, discovers the playable tools and validates input',async()=>{
  const dir=await mkdtemp(path.join(os.tmpdir(),'pawnshop-mcp-'));
  const bridge=createBridge({token:'isolated-mcp-test'});const address=await bridge.listen();
  const config=path.join(dir,'connection.json');await writeFile(config,JSON.stringify({port:address.port,token:'isolated-mcp-test'}));
  const client=new Client({name:'companion-integration-test',version:'0.1.0'});
  const transport=new StdioClientTransport({command:process.execPath,args:[path.join(root,'harness/mcp.mjs')],env:{...process.env,PAWNSHOP_COMPANION_CONFIG:config},stderr:'pipe'});
  try{
    await client.connect(transport);
    const listed=await client.listTools();const names=listed.tools.map(t=>t.name);
    assert.ok(names.includes('companion_wait_events'));assert.ok(names.includes('companion_swap_battery'));
    assert.ok(names.includes('companion_inspect_containers'));assert.ok(names.includes('companion_find_item'));
    assert.ok(names.includes('companion_item_action'));assert.ok(names.includes('companion_machine_control'));assert.ok(names.includes('companion_move_item'));
    assert.ok(names.includes('companion_read_journal'));assert.ok(names.includes('companion_open_journal'));assert.equal(names.length,26);
    const invalid=await client.callTool({name:'companion_say',arguments:{}});assert.equal(invalid.isError,true);
  }finally{await client.close();await bridge.close();await rm(dir,{recursive:true,force:true});}
});
