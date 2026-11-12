# Codex 安装说明

这是「当铺搭子」的 Codex MCP 接入。请下载仓库 Releases 中的完整接入包，不要只取这个文件夹。已安装的 Node.js 需要 22 或更新版本。

## 交给 Agent 安装

把仓库链接 <https://github.com/Fat1aoGod/pawnshop-companion> 粘贴给你正在使用的 Codex，说：

> 帮我安装当铺搭子的 Codex 接入。阅读 harness/README.md，下载最新接入包，解压到固定目录并注册 MCP。保留我现有的会话和配置，装好告诉我在游戏里怎么开启。

## 给安装 Agent

1. 从仓库最新 Release 下载名称包含「接入包」的 ZIP，解压到固定、可写的普通文件夹。完整保留 `bridge`、`prototype`、`harness`、`node_modules` 等目录。不要放进游戏 Mods 或 Steam Workshop 目录。
2. 确认本机 Node.js >=22。发布包已含依赖，无需运行第三方安装脚本；源码包才需要安装依赖。
3. 在接入包根目录运行 `node scripts/setup.mjs`。它只初始化本机配对配置，生成连接码和当前路径的 MCP 配置，不会新建模型会话。
4. 用 Codex 自带设置或 CLI 添加名为 `pawnshop_companion` 的本地 MCP：命令为实际 `node.exe` 路径，唯一参数为实际接入包 `harness/mcp.mjs` 的完整路径。CLI 语法：

```powershell
codex mcp add pawnshop_companion -- node "实际接入包目录/harness/mcp.mjs"
```

如已有同名 MCP，先读现有条目，仅更新本插件；保留其他服务器、模型、权限等配置。工具超时设为 65 秒，启动超时 20 秒。

5. 提醒玩家订阅游戏端：<https://steamcommunity.com/sharedfiles/filedetails/?id=3838517317>。在游戏模组列表启用并重启游戏。进店后右键伙伴 → 显示 → 粘贴连接码 → 保存 → 开始陪玩。
6. 重新加载 Codex MCP，检查工具列表能读到 `companion_list_games`。让玩家在希望陪玩的现有会话里说「接入我的游戏，来陪我看店」。不要擅自从另一会话开始聊天或创建替代会话。

连接码只展示给本机玩家，不上传到 GitHub 或公开反馈。正常更新保留 `runtime/connection.json`，不需要重做配对。

完整说明：[README](../README.md)；MCP 配置细节：[docs/CODEX.md](../docs/CODEX.md)。
