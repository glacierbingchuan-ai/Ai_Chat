# 🤖 AI Chat - AI聊天机器人框架

<div align="center">

[![.NET Version](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=.net&logoColor=white)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%20only-0078D6?style=for-the-badge&logo=windows&logoColor=white)]()
[![License](https://img.shields.io/badge/License-GPL%20v3-blue?style=for-the-badge)](LICENSE)
[![Version](https://img.shields.io/badge/Version-v1.4.0-blue?style=for-the-badge)]()
[![Protocol](https://img.shields.io/badge/Protocol-OneBot%20v11-00A1D6?style=for-the-badge)]()

**基于 .NET 8.0 开发的AI聊天机器人框架**

> 🚧 **跨平台支持正在推进中**，当前版本仅支持 Windows 系统

<img src="https://raw.githubusercontent.com/glacierbingchuan-ai/Character_Cards/refs/heads/main/image/miku.png" width="200" alt="miku">

🌟 支持多模态对话 · 向量数据库 · 插件系统 · 可视化控制面板 🌟

---

📢 官方交流群
🎉 QQ群：**1071354740**
[![QQ群](https://img.shields.io/badge/QQ群-1071354740-12B7F5?style=for-the-badge&logo=tencent-qq&logoColor=white)](https://qm.qq.com/q/SVLceBTx2E)

💬 最新资讯 / 技术答疑 / 插件分享 / 问题反馈
</div>

---

## 📋 目录

- [项目简介](#-项目简介)
- [功能特性](#-功能特性)
- [系统架构](#-系统架构)
- [快速开始](#-快速开始)
- [配置说明](#-配置说明)
- [更新日志](#-更新日志)
- [技术支持](#-技术支持)

---

## 🎯 项目简介

**AI Chat** 是一个功能强大、易于扩展的AI聊天机器人框架，采用现代化的架构设计，支持多种大语言模型（LLM）接入。通过 OneBot v11 协议与QQ、Discord等平台的消息协议端对接，提供丰富的交互功能和可视化配置界面。

### ✨ 核心亮点

| 特性 | 描述 |
|------|------|
| 🧠 **智能对话** | 支持 OpenAI 兼容 API，可接入 GPT、Claude 等多种模型 |
| 🖼️ **多模态支持** | 图片识别与处理，支持视觉模型对话 |
| 📊 **向量数据库** | 基于 Embedding 的智能上下文检索与记忆 |
| 🔌 **插件系统** | 灵活的插件架构，支持热插拔和能力声明 |
| 🎛️ **可视化面板** | 美观的 Web 控制面板，实时监控系统状态 |
| 👥 **多用户支持** | 支持私聊和群聊，独立用户配置 |

---

## 🚀 功能特性

### 🤖 大语言模型支持

- ✅ **OpenAI 兼容 API** - 支持任何 OpenAI 格式的 API 端点
- ✅ **本地模型支持** - 通过 LLamaSharp 支持本地向量模型运行
- ✅ **多模态对话** - 支持图片识别与视觉理解
- ✅ **流式响应** - 实时流式输出，响应更迅速
- ✅ **上下文管理** - 智能上下文压缩与持久化

### 📊 向量数据库系统 (v1.4.0+)

```
┌─────────────────────────────────────────────────────────┐
│                    向量数据库架构                         │
├─────────────────────────────────────────────────────────┤
│  用户消息 → Embedding 模型 → 向量表示 → SQLite 存储       │
│       ↑                                              │
│       └──────── 相似度检索 ← 语义匹配 ← 上下文增强 ──────┘
└─────────────────────────────────────────────────────────┘
```

- 🔍 **智能检索** - 基于语义的上下文检索
- 💾 **本地 Embedding** - 无需 API 的本地向量计算
- ⚡ **相似度阈值** - 可配置的匹配精度
- 📈 **Top-K 检索** - 返回最相关的 K 条记录

### 🔌 插件系统

```csharp
// 插件接口示例
public interface IPlugin : IDisposable
{
    string Id { get; }                    // 唯一标识
    string Name { get; }                  // 插件名称
    Version Version { get; }              // 版本号
    string Author { get; }                // 作者
    string Description { get; }           // 描述
    PluginState State { get; }            // 运行状态
    
    void Initialize(PluginContext context);
    void Start();
    void Stop();
    
    // 能力声明 (v1.4.0+)
    List<PluginCapability> GetCapabilities();
    PluginCapabilityResult InvokeCapability(string name, Dictionary<string, object> parameters);
}
```

**内置能力类型：**
- `MessageHandler` - 消息处理器
- `CommandHandler` - 命令处理器
- `Scheduler` - 定时任务
- `DataProvider` - 数据提供者
- `Interceptor` - 消息拦截器

### 🎛️ 可视化控制面板

| 功能模块 | 描述 |
|:---------|:-----|
| 📡 **协议连接** | WebSocket 连接管理，支持自动扫描 |
| 👤 **用户管理** | 多用户配置，独立上下文 |
| 🤖 **模型配置** | LLM 参数实时调整 |
| 💾 **向量数据库** | 可视化向量数据管理 |
| 🔌 **插件管理** | 插件安装/卸载/配置 |
| 📊 **系统监控** | 实时日志与性能监控 |

---

## 🏗️ 系统架构

```
┌─────────────────────────────────────────────────────────────────────┐
│                         AI Chat 系统架构                             │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐          │
│  │ OneBot v11   │◄──►│  WebSocket   │◄──►│  消息处理器   │          │
│  │  协议端       │    │   Client     │    │              │          │
│  │(NapCat/Lgr等)│    │              │    │              │          │
│  └──────────────┘    └──────────────┘    └──────┬───────┘          │
│                                                  │                  │
│  ┌──────────────┐    ┌──────────────┐           ▼                  │
│  │  控制面板     │◄──►│  HTTP Server │    ┌──────────────┐          │
│  │  (Web UI)    │    │   :8080      │◄──►│  LLM Service │          │
│  └──────────────┘    └──────────────┘    └──────┬───────┘          │
│       ▲                                         │                  │
│       │         ┌──────────────┐               ▼                   │
│       └────────►│ Plugin API   │◄─────┌──────────────┐             │
│                 └──────────────┘      │  Embedding   │             │
│                                        │   Service    │             │
│  ┌──────────────┐    ┌──────────────┐  └──────────────┘             │
│  │  插件系统     │◄──►│ PluginManager│         │                    │
│  │ (.dll)       │    │              │         ▼                    │
│  └──────────────┘    └──────────────┘  ┌──────────────┐             │
│                                        │  Vector DB   │             │
│  ┌──────────────┐    ┌──────────────┐  │  (SQLite)    │             │
│  │   SQLite     │◄──►│   Database   │  └──────────────┘             │
│  │              │    │   Service    │                               │
│  └──────────────┘    └──────────────┘                               │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### 核心模块说明

| 模块 | 路径 | 职责 |
|-----|------|------|
| `Program` | `Program.cs` | 应用程序入口，服务初始化 |
| `LLMService` | `Services/LLMService.cs` | 大语言模型交互服务 |
| `MessageHandler` | `Services/MessageHandler.cs` | 消息路由与处理 |
| `PluginManager` | `Plugins/PluginManager.cs` | 插件生命周期管理 |
| `ControlPanelServer` | `Services/ControlPanelServer.cs` | Web 控制面板服务 |
| `VectorContextManager` | `Services/VectorContextManager.cs` | 向量数据库管理 |
| `WebSocketClient` | `Services/WebSocketClient.cs` | 协议端通信客户端 |

---

## ⚡ 快速开始

### 环境要求

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 或更高版本
- Windows 10/11 系统（Linux 支持正在开发中）
- OneBot v11 协议端（如 [NapCat](https://github.com/NapNeko/NapCatQQ)、[Lagrange](https://github.com/LagrangeDev/Lagrange.Core)）

### 安装步骤

#### 1️⃣ 克隆仓库

```bash
git clone https://github.com/your-repo/AI_Chat.git
cd AI_Chat
```

#### 2️⃣ 启动应用

```bash
dotnet run --framework net8.0-windows
```

> 📝 **注意**：Linux 版本正在开发中，目前仅支持 Windows 系统。

#### 3️⃣ 通过控制面板配置（推荐）

启动后，控制台会显示控制面板地址：

```
🌐 控制面板已启动: http://localhost:8080?key=xxxxxxxx
```

在浏览器中打开该链接，通过可视化界面配置：
- 🤖 **模型配置** - 设置 LLM API、密钥、参数等
- 📡 **协议连接** - 连接 OneBot 协议端
- 👤 **用户管理** - 添加允许的用户/群聊
- 🔌 **插件管理** - 安装和管理插件

> 💡 **提示**：所有配置都可以通过控制面板完成，无需手动编辑配置文件。

#### 4️⃣ 手动配置（可选）

如需手动配置，可编辑 `AI_Chat/config.json`：

```json
{
  "llmModelName": "gpt-4",
  "llmApiBaseUrl": "https://api.openai.com/v1",
  "llmApiKey": "your-api-key",
  "llmMaxTokens": 1024,
  "llmTemperature": 0.9,
  "llmTopP": 0.85,
  "websocketServerUri": "ws://localhost:3000",
  "allowedUserIds": [123456789],
  "allowedGroupIds": []
}
```

---

## ⚙️ 配置说明

### 主配置文件 (`config.json`)

| 配置项 | 类型 | 默认值 | 说明 |
|-------|------|--------|------|
| `llmModelName` | string | - | LLM 模型名称 |
| `llmApiBaseUrl` | string | - | API 基础地址 |
| `llmApiKey` | string | - | API 密钥 |
| `llmMaxTokens` | int | 1024 | 最大生成令牌数 |
| `llmTemperature` | float | 0.9 | 采样温度 (0-2) |
| `llmTopP` | float | 0.85 | 核采样参数 |
| `embeddingModelName` | string | text-embedding-3-small | Embedding 模型 |
| `websocketServerUri` | string | ws://localhost:3000 | 协议端 WebSocket 地址 |
| `websocketToken` | string | - | 认证令牌 |
| `maxContextRounds` | int | 10 | 最大上下文轮数 |
| `allowedUserIds` | array | [] | 允许的用户 QQ 号 |
| `allowedGroupIds` | array | [] | 允许的群号 |
| `useVectorContext` | bool | false | 启用向量上下文 |
| `vectorDbSimilarityThreshold` | float | 0.2 | 向量相似度阈值 |
| `vectorDbTopK` | int | 10 | 返回的最大向量数 |
| `rateLimitTimeWindow` | int | 60 | 限流时间窗口(秒) |
| `rateLimitMaxRequests` | int | 10 | 最大请求次数 |

### 用户配置文件

每个用户拥有独立的配置文件，位于 `UserData/{QQ号}/user_config.json`：

```json
{
  "systemPrompt": "自定义人设提示词",
  "completenessLevel": "Standard",
  "useVectorContext": true,
  "customSettings": {}
}
```

---

## 📝 更新日志

### v1.4.0 (正式版) - 最新版本 🎉

> **重大更新：跨平台支持与向量数据库**

- 🆕 框架升级到 **.NET 8.0**，支持跨平台部署
- 🆕 新增**向量数据库系统**，支持基于 Embedding 的智能上下文检索
- 🆕 新增**本地 Embedding 支持**（基于 LLamaSharp，无需 API）
- 🆕 新增**图片识别与处理**，支持多模态对话
- 🆕 新增**请求速率限制器**
- 🔄 数据库从文件存储迁移到 **SQLite**
- 🔄 插件系统新增**能力声明（PluginCapability）**机制
- 🔄 日志系统升级为 **Serilog**

### v1.3.0 (正式版)

- 支持多用户与群聊
- 新增插件沙箱功能
- 修改插件接口规范

### v1.2.0 (正式版)

- 支持插件广场
- 支持自定义插件
- UI 优化

### v1.1.0 (正式版)

- 支持上下文持久化
- 新增角色卡广场
- 新增聊天记录气泡显示

### v1.0.0 (正式版)

- 实现核心互动、人设定制、多模态表达及可视化配置等全部核心功能

---

## 🤝 技术支持

### 💬 加入讨论

欢迎加入官方 QQ 群，获取最新资讯、技术支持和插件分享！

### 📧 问题反馈

- 🐛 **Bug 报告** - 请使用 GitHub Issues
- 💡 **功能建议** - 欢迎提交 PR 或在讨论区留言
- 📖 **文档改进** - 文档错误请直接提 Issue

### 🙏 致谢

感谢以下用户对项目的贡献：

- [@小叮铛挡档] (2245549440) - 向量数据库建议
- [@周培智💭💡🎈] (3026418933) - 多模态与能力声明建议

---

## 📄 开源协议

本项目采用 [GNU General Public License v3.0 (GPL v3)](LICENSE) 开源协议。

```
AI Chat - AI聊天机器人框架
Copyright (C) 2024 AI Chat Contributors

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
```

---

<div align="center">

**⭐ 如果这个项目对你有帮助，请给我们一个 Star！⭐**

Made with ❤️ by Glacier

</div>