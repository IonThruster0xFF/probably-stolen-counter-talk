# Counter Talk / 柜台闲谈

[GitHub 仓库](https://github.com/IonThruster0xFF/probably-stolen-counter-talk) · [下载发行版](https://github.com/IonThruster0xFF/probably-stolen-counter-talk/releases) · [反馈与建议](https://github.com/IonThruster0xFF/probably-stolen-counter-talk/issues)

为《深空当铺 / Probably Stolen》的普通顾客接入大语言模型，让你在柜台前自由聊天、议价，并通过原版交易界面完成买卖。

**实验版 0.3.12。默认使用 DeepSeek，可在模组齿轮设置中自定义完整 HTTPS Chat Completions URL、模型名称、API-key 和超时时间。需要自备模型服务与额度。**

## 能做什么

- F6 打开文本框，Enter 发送，Alt+Enter 换行；NPC 回复使用原版对话气泡。
- 结合原版对白、公开身份、物品完整标签与液量、所有权、阵营声望和实时预算回答。
- 一次谈整组物品的价格，在每件物品上应用谈价修正。原组合完整时可以继续加购其他物品，新增物品按原价结算；拿走组合成员会暂停谈价，放回同一物品后恢复。最终由玩家点原版买卖按钮结算。
- 顾客可在限额内调整预算、拒绝交易或谈崩离店。报价历史帮助比较前后条件。
- 记录实际成交；读取当前公开报纸新闻，作为可能相关的聊天背景。
- 根据游戏语言回答。剧情、通缉及受保护特殊角色不开放 AI 会话。

模型有自主判断，也可能说错话。可执行的交易最终受本地代码和游戏状态校验约束；不保证所有自然语言推理正确。

## 安装与配置

1. 退出游戏，把公开发行包中的 `CounterTalk` 文件夹放到游戏目录的 `Mods` 下（朋友私测包若自带 `Mods` 层，则合并该文件夹）。
2. 确认存在 `Mods/CounterTalk/manifest.xml` 和 `Mods/CounterTalk/CounterTalk.dll`。
3. 启动游戏，在原版模组管理中审核并启用，按提示重启。
4. 点击模组齿轮，填写自己的 API-key。默认 URL 为 `https://api.deepseek.com/chat/completions`，默认模型为 `deepseek-flash`；模型 ID 必须是你的服务实际支持的名称。
5. 在店里面对普通顾客按 F6。不要把源码仓库 ZIP 直接当成发行包安装。

使用游戏原生模组系统，无需 BepInEx/MelonLoader。其他 Chat Completions 服务需兼容本模组的 JSON 输出请求，未全面测试。

## 数据、费用与测试状态

仅在玩家使用 AI 对话时向配置的模型供应商发送请求，包含输入、对话上下文、顾客/物品状态、公开新闻等。模型费用由使用者的 API 账户承担。Key 输入框遮罩，但原版配置保存在本机明文文件；不要把配置提交到仓库或贴进反馈。

模组声明 `network` 和 `reflection-external`。买卖、预算和声望会影响存档；建议先在独立测试存档体验。没有跨游戏重启的长期聊天记忆，好感度当前按顾客类型聚合。尚未全面验证与其他模组组合使用。

2026-11-01：使用本地 Steam Build 26337366 程序集编译成功，196 项断言通过，原版离线扫描器未拒绝。断言以策略及模拟游戏桥接测试为主，**不等于完整实机验证、独立安全审计或模型回复正确性保证**。之前开发基于 Build 26284217；清单内部版本为 0.46D。

## AI 辅助制作声明

本模组大量使用 AI 辅助编写代码、提示词与文档。**目前缺乏充分、系统的人工代码 review，也尚未经过独立安全审计。** 我们做了编译、自动化测试和有限实际试玩，但仍可能存在逻辑错误、兼容性问题、交易异常或不自然的回复。欢迎帮助审查代码、复现问题和改进实现。

## 转载、修改与交流

欢迎转载、修改、制作分支、翻译和分享改进，也欢迎任何有趣的想法与意见。本项目采用 [MIT 许可证](LICENSE)，允许使用、修改、再发布及商业使用。请保留作者署名、来源链接与许可说明，并注明修改内容，不要把衍生版本冒充原作者维护的版本。游戏本体与第三方素材的权利不属于本项目。

提交 Issue 时请附游戏版本、模组版本、模型 ID、复现步骤和截图；日志发送前删除敏感信息。不要提交 API-key、个人配置或游戏程序集。

## 从源码构建

需要 Windows、.NET SDK 8 和合法安装的游戏：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Probably Stolen"
```

输出在 `dist/CounterTalk`。添加 `-Install` 可在游戏关闭时安装。本仓库不包含游戏 DLL、反编译游戏源码、个人配置或模型密钥。

`src/CounterTalk` 为模组源码，`tests` 为测试，`tools/ScanCheck` 为原版扫描器的离线调用工具，`examples/system-prompt-0.3.11.txt` 为 0.3.11 提示词副本；最新提示词见 `src/CounterTalk/Prompt.cs`。
