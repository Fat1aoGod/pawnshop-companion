import {localConfig,root} from '../bridge/server.mjs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';

export function pairingCode({port,token}){
  if(!Number.isInteger(port)||port<1024||port>65535||!/^[a-f0-9]{48}$/.test(token))throw new Error('连接配置无效，请检查 runtime/connection.json。');
  return `PS1:${port}:${token}`;
}
if(process.argv[1] && path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
  const code=pairingCode(localConfig());
  console.log(`当铺搭子 · 首次连接\n\n你的本机连接码：\n${code}\n\n进店后右键伙伴 → 显示 → 粘贴连接码 → 保存。\n也可以在主菜单 → 模组 → 当铺搭子 → 齿轮里填写。\n这串码只留在自己电脑，不用发到工坊或聊天群。\n\nCodex 接入设置：\n`);
  console.log(`[mcp_servers.pawnshop_companion]\ncommand = ${JSON.stringify(process.execPath)}\nargs = [${JSON.stringify(path.join(root,'harness/mcp.mjs'))}]\nstartup_timeout_sec = 20\ntool_timeout_sec = 65\n`);
}
