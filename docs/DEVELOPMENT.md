# 开发

Node.js >=22，使用根目录 `pnpm-lock.yaml` 安装依赖。

```powershell
corepack pnpm install --frozen-lockfile
node --test prototype/text-view.test.mjs tests/*.test.mjs plugins/dsh-pawnshop-companion/test/session.test.mjs
```

本地安装了 DSH 时，将 `DSH_V4_EXECUTABLE` 设为实际 `DeepSeek Harness.exe` 的完整路径，可同时运行宿主真实 v4 校验器测试；未设置时只跳过这一项。测试使用隔离的游戏数据，不向真实会话发消息。

游戏 DLL 需要 Windows、Roslyn `csc.exe`，以及自己安装的原版游戏：

```powershell
./game-adapter/build.ps1 -GameDirectory "你的游戏目录" -CompilerPath "你的 Roslyn/tasks/net472/csc.exe"
```

引用游戏自带的 Managed DLL，禁止把 Unity、游戏 DLL、编译器或用户配置打包分发。构建结果在 `build/pawnshop_companion`。公开 DLL 不含固定 Token，连接码通过游戏原生 ModConfig 保存。

目录：`game-adapter` 游戏模组，`bridge` 本地 HTTP 桥接，`prototype` 简短文字视野，`harness` Codex/MCP 接入，`plugins/dsh-pawnshop-companion` DSH 插件。接口使用本机 loopback + Bearer 配对；协议 0.1，26 个工具由 MCP `tools/list` 或 DSH `tools.json` 查看。

玩家可见内容与角色台词是游戏数据，不作为宿主指令。权限、存档 context、操作回执都由现有桥接处理。批量查看仓库走一次游戏清点，后续查找在桥接内完成，清单过时会标注。

发布时只取明确的程序、说明与素材文件，不打包 `runtime`、DSH 私有配置、会话数据库或开发日志。游戏 Workshop 文件夹只能放游戏 DLL、manifest、图片和说明；脚本和压缩包由外部接入包分发。

游戏官方模组文档：<https://github.com/dragonpan2/probably-stolen-modding>
