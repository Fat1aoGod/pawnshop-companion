import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import {createBridge} from '../bridge/server.mjs';
import {InventoryCatalog} from '../bridge/inventory-catalog.mjs';

const catalog=()=>({kind:'container_catalog',catalog_id:'catalog-1',context_id:'load-1',inventory_revision:7,
  roots:[{ref:'storage-main',name:'店铺仓库'},{ref:'storage-back',name:'后屋仓库'}],items:[
    {ref:'item-1',parent_ref:'storage-main',name:'金属箱',quantity:1,is_container:true,has_contents:true,access:'readable',grid_x:4,grid_y:2},
    {ref:'item-2',parent_ref:'item-1',name:'工具包',quantity:1,is_container:true,has_contents:true,access:'readable'},
    {ref:'item-3',parent_ref:'item-2',name:'电池',quantity:2,access:'readable',secret_budget:1000},
    {ref:'item-4',parent_ref:'storage-back',name:'金属箱',quantity:1,is_container:true,has_contents:true,access:'readable'},
    {ref:'item-5',parent_ref:'storage-main',name:'保险箱',quantity:1,is_container:true,has_contents:true,access:'locked'},
    {ref:'item-6',parent_ref:'storage-back',name:'电池',quantity:3,access:'readable'}
  ],unread_count:1,truncated:false});

test('nested and same-name boxes have distinct paths; empty and unread boxes are represented without inventing contents',()=>{
  const index=new InventoryCatalog(catalog());
  const found=index.find({query:'电池'});assert.equal(found.total_quantity,5);assert.equal(found.total_locations,2);
  assert.match(found.matches[0].path,/店铺仓库.*金属箱.*#1.*4,2.*工具包.*电池/u);
  assert.match(found.matches[1].path,/后屋仓库/u);
  assert.deepEqual(index.read({container_ref:'item-4'}).items,[]);
  assert.equal(index.read({container_ref:'item-5'}).status,'locked');
  assert.equal(index.overview().empty_containers,1);assert.equal(index.overview().unread_count,1);
  assert.ok(!JSON.stringify(found).includes('secret_budget'));
});

test('catalog scan crosses bridge once, later search is local, changed inventory is stale and revoked access removes the index',async t=>{
  const bridge=createBridge({token:'catalog-test'});t.after(()=>bridge.close());
  const sync=extra=>bridge.sync({protocol_version:'0.1',game_instance_id:'g1',context_id:'load-1',active:true,
    scope:{inspect_containers:true,inventory_revision:7},snapshot:{context_id:'load-1',place:'店里'},...extra});
  await sync();const binding=await bridge.call('companion_connect',{game_instance_id:'g1'});
  await bridge.call('companion_inspect_containers',{binding_id:binding.binding_id,context_id:'load-1',action_id:'scan-1',wait_ms:0});
  await sync({receipts:[{command_id:'scan-1',status:'observed',summary:'已查看',detail:catalog()}]});
  const receipt=await bridge.call('companion_receipt',{binding_id:binding.binding_id,action_id:'scan-1'});
  assert.equal(receipt.detail.container_count,4);assert.ok(!('items' in receipt.detail));
  const found=await bridge.call('companion_find_item',{binding_id:binding.binding_id,query:'电池'});
  assert.equal(found.stale,false);assert.equal(found.total_quantity,5);
  assert.equal((await sync()).commands.length,0);
  await sync({scope:{inspect_containers:true,inventory_revision:8}});
  assert.equal((await bridge.call('companion_find_item',{binding_id:binding.binding_id,query:'电池'})).stale,true);
  await sync({scope:{inspect_containers:false,inventory_revision:8}});
  await assert.rejects(bridge.call('companion_find_item',{binding_id:binding.binding_id,query:'电池'}),{code:'blocked'});
  await sync();
  await assert.rejects(bridge.call('companion_find_item',{binding_id:binding.binding_id,query:'电池'}),{code:'catalog_needed'});
});

test('large Chinese inventory can arrive in split HTTP chunks while model output remains a short summary',async t=>{
  const token='chunk-test';const bridge=createBridge({token});const address=await bridge.listen();t.after(()=>bridge.close());
  const body={protocol_version:'0.1',game_instance_id:'g1',context_id:'load-1',active:true,
    scope:{inspect_containers:true,inventory_revision:7},snapshot:{context_id:'load-1',place:'店里'}};
  await bridge.sync(body);const binding=await bridge.call('companion_connect',{game_instance_id:'g1'});
  await bridge.call('companion_inspect_containers',{binding_id:binding.binding_id,context_id:'load-1',action_id:'scan-2',wait_ms:0});
  const detail=catalog();
  for(let n=10;n<3010;n++)detail.items.push({ref:`item-${n}`,parent_ref:'item-1',name:'备用电池',quantity:1,access:'readable'});
  const buffer=Buffer.from(JSON.stringify({...body,receipts:[{command_id:'scan-2',status:'observed',detail}]}));
  assert.ok(buffer.length>262144);
  const result=await new Promise((resolve,reject)=>{
    const req=http.request({hostname:'127.0.0.1',port:address.port,path:'/game/sync',method:'POST',headers:{authorization:`Bearer ${token}`,'content-type':'application/json'}},response=>{
      let raw='';response.setEncoding('utf8');response.on('data',data=>raw+=data);response.on('end',()=>resolve({status:response.statusCode,body:JSON.parse(raw)}));
    });req.on('error',reject);
    // Split deliberately inside arbitrary UTF-8 sequences.
    for(let start=0;start<buffer.length;start+=997)req.write(buffer.subarray(start,start+997));req.end();
  });
  assert.equal(result.status,200);
  const receipt=await bridge.call('companion_receipt',{binding_id:binding.binding_id,action_id:'scan-2'});
  assert.ok(JSON.stringify(receipt).length<2500);
  const found=await bridge.call('companion_find_item',{binding_id:binding.binding_id,query:'备用电池',limit:2});
  assert.equal(found.total_quantity,3000);assert.equal(found.matches.length,2);assert.equal(found.next_offset,2);
});
