# DSH 安装说明

这是「当铺搭子」的 DSH 接入插件。它使用你选定的已有会话，不要求额外的模型 API。已在 DSH 0.2.2-rc.1 测试。

## 交给 Agent 安装

把仓库链接 <https://github.com/Fat1aoGod/pawnshop-companion> 粘贴给 DSH 里的 Agent，说：

> 帮我安装当铺搭子的 DSH 接入。阅读 plugins/dsh-pawnshop-companion/README.md，下载最新接入包，以当前会话作为陪玩会话。只安装插件和填写插件配置，保留我的会话、人设、记忆及其他插件。装好后告诉我需要重启哪些东西。

## 给安装 Agent

1. 从最新 Release 下载文件名以 `-connectors.zip` 结尾的接入包，完整解压到固定、可写的普通文件夹，不要放到游戏模组目录。插件继续保留在 `plugins/dsh-pawnshop-companion`，它会沿相对路径找到桥接。
2. 先确认**实际运行的 DSH 版本、CLI 和 profile**，不要凭旧安装目录猜。结束当前工具等待后退出宿主，再进行安装；不要关闭玩家未授权关闭的其他程序。
3. 用宿主的本地插件安装功能添加该目录。CLI 示例，路径与 profile 使用当前环境的实际值：

```powershell
dsh plugin --profile desktop add "link:实际接入包目录/plugins/dsh-pawnshop-companion" --offline --ignore-scripts
```

4. 确认 profile 已启用 `dsh-pawnshop-companion` bundle。插件随附 `cordis.patch.yml`。若当前宿主需要手动追加 bundle，先备份目标 profile，只在 `dsh.profile.bundles` 数组追加此项；保留所有其他设置与插件。
5. 将插件的 `config.example.json` 复制为 `config.json`，填写**玩家指定的现有会话 ID** 到 `sessionId`。可用当前会话 ID 时直接使用；无法可靠确认时向玩家询问，不根据名字猜。`displayName` 允许玩家本地自行设置，公开默认「DeepSeek 搭子」。
6. 正常重启 DSH。游戏端需订阅 <https://steamcommunity.com/sharedfiles/filedetails/?id=3838517317>，启用后重启游戏。
7. 在完整接入包根目录运行 `node scripts/setup.mjs` 生成连接码；本机 Node.js 需 >=22。进店右键伙伴 → 显示 → 保存连接码 → 开始陪玩。DSH 插件会等待游戏消息，不需要新开模型会话。

如使用宿主自带 Node.js，须先确认实际可执行文件。需要时将其路径填到插件 `config.json` 的 `nodePath`。插件目录与完整接入包分开存放时，另将 `projectRoot` 填为接入包根目录。

仅修改插件自己的配置和安装所需的 profile 项。不要导出、重置、迁移、压缩或改写聊天、人设、记忆、会话数据库，也不要为安装另建替代会话。更新时保留 `config.json`、`binding-state.json` 以及根目录 `runtime/connection.json`；这些文件不上传。

宿主会话格式有版本差异；不要把已验证的消息来源包装随意改成通用 `kind: plugin`。本插件测试包含真实 v4 校验器的可选检查。

进一步的配置项：[docs/DSH.md](../../docs/DSH.md)。
