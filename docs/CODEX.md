# Codex 接入

使用完整接入包，在本机 Node.js 22 或更新版本下运行。无需给 Mod 填模型 API Key，它直接给现有 Codex 会话提供游戏工具。

双击根目录 `首次连接.cmd`，会显示当前电脑的连接码和 MCP 配置。把连接码保存到游戏伙伴面板「显示」页，再将生成的配置加到 Codex 的 MCP 设置中。

配置示意，实际路径以首次连接窗口为准：

```toml
[mcp_servers.pawnshop_companion]
command = "node"
args = ["你的接入包目录/harness/mcp.mjs"]
startup_timeout_sec = 20
tool_timeout_sec = 65
```

Windows 路径可以用正斜杠，或使用首次连接窗口生成的转义格式。不要把配置中的示意路径原样照抄。

在游戏里点「开始陪玩」，再对希望陪玩的现有 Codex 会话说：

> 用当铺搭子接入我的游戏，以你现在的身份来陪我看店。先看看现场，不急着干活，有什么想法也可以聊。

MCP 会自动启动本机桥接，不用额外开一个常驻终端。只让一个会话连接这一局，换伙伴时先在游戏里停止，再开始。

Codex 官方设置说明：<https://learn.chatgpt.com/docs/extend/mcp?surface=app>
