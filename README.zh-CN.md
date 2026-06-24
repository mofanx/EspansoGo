<p align="center">
  <a href="README.md">English</a> | <strong>简体中文</strong>
</p>

# EspansoGo

<p align="center">
  <a href="https://github.com/mofanx/EspansoGo/issues">报告 Bug</a>
  ·
  <a href="https://github.com/mofanx/EspansoGo/issues">功能请求</a>
  ·
  <a href="https://github.com/mofanx/EspansoGo/releases">发布版本</a>
  ·
  <a href="https://espanso.org/docs/get-started/">Espanso 文档</a>
</p>

一款基于配置的 Android 文本扩展应用，兼容 [espanso](https://espanso.org) 配置文件。通过 YAML 定义触发词和替换内容，让 EspansoGo 替你自动输入。

> 本项目受 [lochidev](https://github.com/lochidev) 开发的 [Expandroid](https://github.com/lochidev/Expandroid) 启发并重写，采用 .NET MAUI + Blazor Hybrid 重新实现。沿用同一开源协议。

<p align="center">
  <a href="https://github.com/mofanx/EspansoGo/releases/latest"><img src="https://raw.githubusercontent.com/andOTP/andOTP/master/assets/badges/get-it-on-github.png" height="80"></a>
</p>

## 功能特性

- **永久免费开源** — 无广告、无追踪
- **无限扩展** — 触发词和匹配数量无上限
- **Espanso 兼容** — 直接复用现有 YAML 配置文件
- **变量类型** — `echo`、`date`、`random`、`clipboard`、`choice`、`form`、`shell`、`script`、`http`、`javascript`、`match`
- **表单支持** — 多行文本、选择列表
- **跨设备同步** — WebDAV 和 Git 同步，保持与桌面端 espanso 配置一致
- **无需网络权限** — 数据完全留在设备本地
- **多语言界面** — 支持英文和中文，更多语言持续添加中

## 快速开始

1. 从 [Releases](https://github.com/mofanx/EspansoGo/releases/latest) 下载最新 APK
2. 安装并授予无障碍服务权限
3. 导入 espanso YAML 配置，或在编辑器中创建新的匹配规则
4. 在任意位置输入触发词，自动展开替换内容！

> **应该下载哪个 APK？**
> - `arm64-v8a` — 推荐，覆盖 95% 的现代设备
> - `armeabi-v7a` — 旧款 32 位设备
> - `x86_64` — 模拟器 / Chromebook
> - `fat` — 通用版本，兼容所有设备（体积较大）

## 支持的日期格式

支持以下 chrono 日期时间格式：

```
%Y, %m, %b, %B, %h, %d, %e, %a, %A, %j, %w, %u, %D, %F, %H, %I, %p, %M, %S, %R, %T, %r
```

还可以使用 C# `DateTime.ToString()` 格式字符串进行进一步自定义。

## 注意事项

- Espanso YAML 文件可能需要多次尝试才能正确解析，请确保符合 YAML 规范。
- 由于 Google 的安全限制，剪贴板扩展在 Android 10+ 上无法直接使用。v1.0 起提供了替代方案。
- 表单功能包括多行、选择和列表。espanso 文档中"Using Forms with Script and Shell extensions"部分不支持。
- 未在 Android 12 以下版本测试。

## 构建

本项目使用 .NET MAUI + Blazor Hybrid + MudBlazor 技术栈。

```bash
# 安装 MAUI 工作负载
dotnet workload install maui-android

# 还原依赖
dotnet restore src/EspansoGo.csproj

# 构建 Debug
dotnet build src/EspansoGo.csproj -c Debug -f net9.0-android35.0

# 构建 Release（需要签名密钥环境变量）
dotnet build src/EspansoGo.csproj -c Release -f net9.0-android35.0
```

详细的 CLI 发布说明请参考 [微软 MAUI 部署指南](https://learn.microsoft.com/en-us/dotnet/maui/android/deployment/publish-cli)。

### CI/CD

- **推送至 main** → 自动构建，产物可下载
- **打标签 `v*`** → 自动发布 GitHub Release，包含按架构签名的 APK

## 技术栈

- [.NET 9 MAUI](https://learn.microsoft.com/dotnet/maui/) — 跨平台框架
- [Blazor Hybrid](https://learn.microsoft.com/aspnet/core/blazor/hybrid/) — UI 渲染
- [MudBlazor](https://mudblazor.com/) — Material Design 组件库
- [YamlDotNet](https://github.com/aaubry/YamlDotNet) — YAML 解析

## 开源协议

Apache License 2.0，详见 [LICENSE](LICENSE) 文件。
