# 构建与发布维护

## 自动发布

版本唯一来源是 [Version.props](../Version.props)。先修改版本，再添加同名版本说明（例如 docs/releases/0.5.1.md），提交并推送默认分支。
GitHub Actions 会依次执行离线测试、图标预览、完整插件编译、安装 ZIP 构建及内容验证，然后创建 v版本号 tag 和 Release。
同版本已有正式 Release 时跳过发布，不覆盖已有正式版本；失败留下的 draft 可在重跑时补上传并发布。
也支持手动推送与 Version.props 一致的 v* tag，或在 Actions 页面手动运行。
测试 / 编译 / 打包失败时不发布。Release 先保持 draft，两个附件上传成功后才公开。

普通 push 会构建；只有默认分支和版本 tag 会发布。PR 只跑纯逻辑测试与图标预览。
发布使用 GitHub 自动提供的 GITHUB_TOKEN（contents: write），无需个人访问令牌。
此 token 创建的 tag 不会再递归触发工作流，因此创建 tag 与发布在同一次运行完成。

## 私有编译依赖

公开源仓库不存放游戏程序集。私有 ChenRuoT/AyaHitmarker-BuildRefs 保存从目标游戏的 BepInEx/core 和 BepInEx/interop 提取的 DLL 压缩包，作为编译参考。
CI 通过项目 secret BUILD_REFS_SSH_KEY 使用私有依赖仓库的只读 deploy key。密钥不具有写权限，也不能读取其他仓库。
tools/release/buildrefs.lock.json 锁定私有仓库 commit、压缩包名称和 SHA256；更新依赖时三个字段一起更新。
不使用 Il2CppDumper 的 DummyDll：它们不是 interop 实现。私有依赖 checkout 放在 .buildrefs/，已从 Git 和 MSBuild 的默认 items 中排除。

云端 job 只在主项目的 push / dispatch 使用私有依赖；PR 没有这个访问路径。
不要把构建依赖作为 CI artifact 或 Release 附件上传，安装包仅允许插件、说明和 ReleaseAssets 中指定资源。
更换密钥：生成新 SSH key，把公钥登记为依赖仓库只读 deploy key，私钥写入项目 secret，再删除旧 deploy key。
私钥不入库，初始化所用临时私钥已在配置成功后删除。

## 本地构建和打包

需要 .NET 10 SDK，以及目标游戏已生成的 BepInEx interop：

```powershell
dotnet run --project tests/AyaHitmarker.LogicTests -c Release -warnaserror
dotnet build AyaHitmarker.csproj -c Release -p:DeployToGame=false -warnaserror
pwsh -File tools/release/package.ps1
pwsh -File tools/release/verify-package.ps1 -Archive artifacts/release/AyaHitmarker-v0.5.0-EFT-1.2.0.0.zip
```

默认 GameDir 见 Directory.Build.props，可用 -p:GameDir="E:/OtherEFT" 覆盖。
-p:DeployToGame=false 让发布构建不写入正在运行的游戏。日常 dotnet build 仍按原方式自动部署。
打包不会从游戏安装目录捞文件：它使用新编译的 bin/Release/AyaHitmarker.dll 和已入库 ReleaseAssets，检查 DLL 版本与 Version.props 一致。
ZIP 顶层只有 BepInEx，没有多余版本目录；不带 cfg，不覆盖玩家设置。
输出 ZIP 与 SHA256 文件位于 artifacts/release/，该目录不入库。

## 验证边界

CI 验证可执行逻辑、编译与安装目录/资源完整性。图标预览作为独立 artifact 保存，方便肉眼查看。
Unity 渲染、逐帧动画、音效及 ConfigurationManager 实际排版仍需 [开发记录 §11](DEVELOPMENT.md#diagnostics) 中的 DebugHarness / 游戏验证。
