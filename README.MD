# AI Chat - 专属恋爱向AI女友
现实向恋爱搭子 | 情感共鸣 | 情侣式互动 | 全人设定制

核心说明：不同于机械回复，可像现实女友一样记住聊天细节，自然主动开启互动。

<a href="http://qm.qq.com/cgi-bin/qm/qr?_wv=1027&k=1pOX2q6gKl41GttJcuXDoCuBVlEhWNTS&authKey=PI7D%2FlEFqlD%2B6zLjE7FCTbzsVBig%2BWMy5rCHAlQOIpsRsUZKCA%2FOYzBDzzUA%2FAaf&noverify=0&group_code=1071354740" 
   target="_blank" 
   style="display: inline-block; padding: 6px 16px; background: #12b7f5; color: white; text-decoration: none; border-radius: 4px; font-size: 14px; border: none; cursor: pointer;">
   点击加入交流群
</a>
**QQ群号：1071354740**

---

## 📖 项目简介
AI_Chat 是基于 WebSocket + LLM 技术开发的恋爱向AI女友聊天机器人，核心是还原现实情侣日常互动，支持自定义AI人设，打造专属恋爱搭子，提供沉浸式情侣聊天体验。

- 部署教程：https://www.bilibili.com/video/BV1noFBzZEpD/?spm_id_from=333.337.search-card.all.click&vd_source=33af0efdf7b9a7239c20a467129c48d4
- 效果演示视频：https://www.bilibili.com/video/BV1gsfZBbE4F/?spm_id_from=333.337.search-card.all.click&vd_source=33af0efdf7b9a7239c20a467129c48d4

---

## ✨ 核心功能
- 核心互动：侧重情感陪伴，主动贴合你的情绪
- 人设定制：可自定义姓名、性格、称呼等，适配个人偏好
- 情感化对话：贴合设定输出有温度的回复，适配各类情绪场景
- 多模态表达：文字+表情包，还原真实情侣聊天感受
- 智能记忆：留存聊天细节，保证对话连贯
- 可视化配置：网页端一键操作，无需修改代码
- 日志记录：留存聊天及互动痕迹，便于问题排查

---

## 🛠️ 技术栈
|模块分类|具体技术选型|
|---|---|
|开发语言|C#|
|核心框架|.NET Framework 4.6+|
|通信协议|WebSocket（对接Bot协议端）|
|LLM对接|支持所有OpenAI协议API|
|推荐协议端|NapCatQQ（稳定适配，操作简单）|

---

## 🚀 快速开始
### 🔑 前提条件
1. 安装 .NET Framework 4.6+
2. 准备OpenAI协议LLM API密钥
3. 部署NapCatQQ（WebSocket协议端）

### 📦 部署步骤
1. 安装NapCatQQ，登录机器人QQ账号并开启WebSocket服务器
2. 编译项目并运行AI_Chat.exe
3. 访问控制面板（http://localhost:8080?key=日志中密钥
），配置API、人设及互动规则
4. 与AI聊天，即可触发自然的情侣式互动

---

### 📝 提示词规范
请参考项目自带的提示词编写规范进行配置。

---

## 📁 项目结构
```
AI_Chat/
├── Program.cs          # 主程序文件
├── App.config          # 应用配置文件
├── packages.config     # NuGet包配置文件
├── BotLogs/            # 日志文件夹
│   ├── GeneralLogs/    # 通用日志
│   └── AIContextLogs/  # AI上下文日志
├── bin/                # 编译输出文件夹
├── obj/                # 编译中间文件文件夹
├── meme/               # 表情包文件夹
│   ├── baobao_1.jpg     # 撒娇类表情包
│   ├── love_1.jpg       # 爱意类表情包
│   ├── ok_1.jpg         # 同意类表情包
│   ├── wanan_1.jpg      # 晚安类表情包
│   └── 其他表情包文件...
└── ControlPanel/       # 控制面板前端文件
    └── public/
        ├── index.html       # 控制面板主页
        ├── unauthorized.html # 未授权页面
        ├── css/style.css    # 样式文件
        └── js/script.js     # JavaScript文件
```
---

## ⚙️ 核心配置说明
|配置项|说明|推荐值|
|---|---|---|
|targetUserId|目标账号ID（确保专属互动）|个人实际QQ账号|
|eventActiveProbability|主动互动触发概率|30%|
|llmTemperature|回复情感化随机性|0.9-0.95|
|reminderEnabled|主动记挂小事开关|true|

---

## ❗ 常见问题排查
- AI互动机械：优化提示词，将llmTemperature调整至0.9-0.95
- 未主动提起小事：确认reminderEnabled设为true，调整触发概率及互动时段
- 连接失败：检查NapCatQQ启动状态、WebSocket地址及API密钥正确性
- 其他配置、部署疑问：可加入上方交流群咨询，群内将同步解答问题、分享配置技巧

---

## 📜 开源协议
采用 **GPLv3 开源协议** (https://www.gnu.org/licenses/gpl-3.0.html
)，遵循协议相关规定，可进行修改、分发与二次开发，衍生作品同样需采用GPLv3协议开源，且需保留原项目版权声明。

---

## 📮 版本更新日志

- v1.0.0（正式版）：实现核心情侣互动、人设定制、多模态表达及可视化配置等全部核心功能。
- v1.0.1（正式版）：修复了前端修改API密钥后端不会更新的问题
- v1.0.2（正式版）：修复了一点小BUG，添加了清空上下文按钮






