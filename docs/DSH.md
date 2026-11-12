# DSH 本地插件安装

保留 `plugins/dsh-pawnshop-companion` 在完整接入包中的位置。插件默认沿相对路径找到本机桥接，不依赖作者电脑的目录。

推荐在 DSH 自带的插件管理中添加本地目录。CLI 方式如下，在自己的 DSH CLI 环境里运行（将路径替换成实际位置）：

```powershell
dsh plugin --profile desktop add "link:你的接入包目录/plugins/dsh-pawnshop-companion" --offline --ignore-scripts
```

DSH 的桌面 profile 中需要启用 `dsh-pawnshop-companion` bundle。不同宿主版本的启用界面可能不同；安装依赖和启用 bundle 都完成后，正常重启 DSH 才会加载。

插件随附 `cordis.patch.yml`，包中 `dsh.bundle.patch` 指向它。若手动维护 profile，在 `dsh.profile.bundles` 数组添加字符串 `dsh-pawnshop-companion`，保留原有项目。**不要用别人的完整 profile 文件覆盖自己的文件。**

插件配置只放在插件自己的 `config.json`，从 `config.example.json` 复制：

- `sessionId`：现有陪玩会话的 ID，必填。不会为你创建或替换会话。
- `displayName`：游戏里显示的名字。
- `avatarKey`：`deepseek` 或 `gpt`。
- `autoStart`：是否后台等待游戏，默认 `true`。
- `waitTimeoutMs`：后台等待时长，默认 `45000`。
- `nodePath`：通常不用填。宿主不能启动桥接时，可填本机 Node.js 可执行文件完整路径。
- `projectRoot`：通常不用填。仅在插件与接入包分开存放时填写接入包根目录。

更新前结束当前工具等待并退出 DSH。用新版程序文件替换旧文件时保留自己的 `config.json` 和 `binding-state.json`。不会要求导出人设、记忆或聊天数据库。

插件只向选定会话注册工具；其他会话不会被挂上游戏工具。没有选会话时保持空闲。
