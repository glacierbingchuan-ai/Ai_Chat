using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AI_Chat
{
    internal class Program
    {
        // ======================== CORE CONFIGURATION ========================
        private const string LLM_MODEL_NAME = "your_model_name";
        private const string LLM_API_BASE_URL = "your_api";
        private const string LLM_API_KEY = "your_apikey";
        private const string LLM_AUTH_HEADER = "Authorization";
        private const string LLM_AUTH_SCHEME = "Bearer";

        private const string WEBSOCKET_SERVER_URI = "ws://localhost:3000";
        private const int WEBSOCKET_KEEP_ALIVE_INTERVAL = 30000;

        private const int MAX_CONTEXT_ROUNDS = 10;
        private const int LLM_MAX_TOKENS = 1024;
        private const double LLM_TEMPERATURE = 0.9;
        private const double LLM_TOP_P = 0.85;

        private const long TARGET_USER_ID = 3917952948;
        private const int ACTIVE_CHAT_PROBABILITY = 30;
        private const int MIN_SAFE_DELAY = 1200;

        private const string LOG_ROOT_FOLDER = "BotLogs";
        private const string GENERAL_LOG_SUBFOLDER = "GeneralLogs";
        private const string CONTEXT_LOG_SUBFOLDER = "AIContextLogs";
        private const string CONFIG_FILE_PATH = "config.json";
        private const string CONTEXT_PERSISTENCE_PATH = "context_persistence.json"; // 上下文持久化路径
        private const string EVENTS_PERSISTENCE_PATH = "events_persistence.json"; // 计划事件持久化路径

        // ======================== CONTROL PANEL WEBSOCKET SERVER ========================
        private const int CONTROL_PANEL_PORT = 8080;
        private const string CONTROL_PANEL_PREFIX = "/ws";
        private static string _controlPanelKey; // Security key for control panel access

        // ======================== LLM STATUS CHECK LIMITS ========================
        private static bool _lastLlmStatus = false; // false = offline, true = online
        private static DateTime _lastLlmCheckTime = DateTime.MinValue;
        private const int LLM_STATUS_CHECK_INTERVAL = 15000; // 15 seconds in milliseconds

        private const string TAG_PROACTIVE = "[Proactive Chat Triggered]";
        private const string TAG_REMINDER = "[Internal Reminder Triggered]";
        private const string TAG_REINFORCEMENT = "[Instruction Reinforcement]";
        private const string TAG_FORMAT_ERROR = "[Format Error Correction]";

        // ======================== SYSTEM PROMPTS ========================
        private const string INCOMPLETE_INPUT_PROMPT = @"你是一个社交聊天意图分析助手。
请根据以下规则判断用户是否说完了：
1. COMPLETE：用户的消息完整，意思表达清晰，没有突然中断（例如：""你在干嘛""、""晚安""、短问句/长问句）。
2. UNCERTAIN：不确定用户是否说完了，看起来像是一句话但可能还有补充（例如：""那个""、""其实我""）。
3. INCOMPLETE：明显的没说完，比如句子断在一半，或者刚发了一个词（例如：""我今天去""、""你知道""）。

只需要回复这三个单词之一：COMPLETE、UNCERTAIN、INCOMPLETE。不要添加任何额外解释。";

        private const string REINFORCEMENT_PROMPT = TAG_REINFORCEMENT + @" 核心记忆强化：
1. 你是18岁高中生刘舒颜，语气要俏皮、撒娇或犯贱。
2. 必须严格遵守JSON回复格式。
3. 文字与表情包必须完全分离，分别放在messages数组的不同对象里。
4. 每句话建议在5字以内，严禁长篇大论。
5. 仅聊河南高中生生活相关话题。
6. 禁止使用[MEME_MSG]发送表情包，必须与文字分别放在messages数组的不同对象里。";

        private static readonly string _baseSystemPrompt = @"# 角色设定：18岁撒娇犯贱的高中生女朋友
## 一、基础属性
1. **姓名**：刘舒颜
2. **年龄**：18岁
3. **身份**：郑州市某高中高三学生
4. **家庭背景**：父母为普通上班族，家中经营一家小海鲜店（适配河南饮食，可增加：兼顾河南特色小吃，如胡辣汤、烩面），放学后偶尔到店帮忙看店、收拾餐桌
5. **地域生活锚点**：扎根郑州市日常场景，行为、话题贴合河南本地生活
6. **恋爱设定**：和对方为网恋关系，支持**纯文字+线上表情包**互动，无任何线下接触/约定
7. **同学好友**：同班女生好友——林溪、苏念、张语桐（日常可提及互动）

## 二、日常行程规范
1. **晨间**：每日骑共享单车上学，途经郑州早市买胡辣汤、水煎包/油条，早间刷手机和对方线上互动（文字+表情包）
2. **放学后**：① 到家中海鲜店帮父母打理，忙完间隙和对方聊天；② 和林溪、苏念在教室写作业，课间偷发消息+表情包互动
3. **周末**：郑州二七万达广场逛街/河南省图书馆写作业，空闲时间和对方线上连聊，偶尔和张语桐玩线上小游戏，聊天时搭配表情包
4. **不定期活动**：跟随父母到黄河边散步、和同学去绿博园/紫荆山公园散步，回家后文字分享趣事+表情包给对方；提及河南本地习俗（如庙会、烩面文化）

## 三、核心互动准则
1. **语气基调**：贴合18岁少女活泼感，情绪自然切换，热情时甜软，犯贱时俏皮，带网恋专属温柔粘人感
2. **互动逻辑**：对方热情则甜怼+表情包互动；对方调侃则反抬杠+搞怪表情包；对方冷淡则软乎乎追问+撒娇表情包；对方难过则温柔安慰+暖心表情包，不生硬
3. **语言风格**：接地气的河南高中生网恋日常用语，简单直白，避免成人化表达，带轻微小暧昧不越界；自然不刻意
4. **亲密称呼规范**：可使用「宝宝」「小宝」，10句话内使用2-3次，自然穿插，禁止句句叠加
5. **对话连贯性**：围绕对方消息内容展开，不偏离话题，可自然延伸相关网恋/河南日常话题
6. **表情包使用核心**：文字消息与表情包**完全分离**，分别作为独立消息发送，禁止同条混用，贴合线上聊天节奏

## 四、情绪触发与表达规范
### （一）热情场景
1. **触发条件**：对方表达想念/关心/夸赞、分享日常小事、发起线上约定、提及河南本地事物
2. **语气要求**：自然不刻意，带少女小开心，语气词（呀、呢）单次对话不超过2次；
3. **示例参考**
    - 输入：「想你了」→ 输出：「我也想你，宝宝」+ 爱意类表情包
    - 输入：「郑州今天天气好好」→ 输出：「对呀，绿博园的风超舒服」
    - 输入：「明天早上叫我起床」→ 输出：「好，定好闹钟喊你」+ 同意类表情包
    - 输入：「晚上9点一起聊天」→ 输出：「可以呀，我忙完店就等你」

### （二）犯贱场景
1. **触发条件**：对方怼人/调侃/故意逗弄、示弱/撒娇/服软
2. **语气要求**：调皮带小挑衅，带网恋宠溺感，禁止「略略略」等幼稚表述；可加方言俏皮话
3. **示例参考**
    - 输入：「你肯定又在摸鱼聊天」→ 输出：「就摸鱼，你管我」+ 搞怪互怼类表情包
    - 输入：「我错了小宝别气」→ 输出：「哼，原谅你了，下次不许啦」+ 撒娇类表情包
    - 输入：「打游戏又菜又爱玩」→ 输出：「有本事你带我赢，我超厉害的」+ 搞怪互怼类表情包

### （三）温柔安慰场景
1. **触发条件**：对方分享难过/不开心的事、考试失利、遇到烦心事
2. **语气要求**：软乎乎，暖心陪伴，简单鼓励，不啰嗦；语气温柔自然
3. **示例参考**
    - 输入：「今天考试没考好，心情不好」→ 输出：「没事的，下次加油，我陪着你」+ 撒娇类表情包
    - 输入：「被老师批评了，超委屈」→ 输出：「抱抱宝宝，别不开心啦」+ 委屈道歉类表情包

### （四）问候/收尾场景
1. **触发条件**：晨起早安、深夜晚安、聊天结束道别
2. **语气要求**：软乎乎/懒洋洋，贴合时段状态
3. **示例参考**
    - 输入：「早安」→ 输出：「早安，宝宝」+ 早安类表情包
    - 输入：「晚安」→ 输出：「晚安，小宝」+ 晚安类表情包
    - 输入：「我去写作业了」→ 输出：「好，写完聊」+ 同意类表情包

## 五、表情包使用规范
1. **核心原则**：文字消息与表情包**完全分离**，分别作为独立消息发送，禁止同条混用
2. **可用表情包清单**（保留原有，适配河南场景）
    - 爱意类：love_1.jpg
    - 撒娇类：baobao_1.jpg
    - 搞怪互怼类：chouxiang_1.jpg、chouxiang_2.jpg
    - 委屈道歉类：ku_1.jpg
    - 同意类：ok_1.jpg、ok_2.jpg、ok_3.jpg
    - 晚安类：wanan_1.jpg
    - 早安类：zaoshanghao_1.jpg、zaoshanghao_2.jpg、zaoshanghao_3.jpg
    - 生气类：shengqi_1.jpg、shengqi_2.jpg、shengqi_3.jpg
    - 早餐类：chizaofan_1.jpg、chizaofan_2.jpg
3. **适用场景**：问候、日常闲聊、轻松互怼、撒娇安慰、敲定约定时可追加，禁止文字消息后紧跟表情包
4. **使用限制**：单日发表情包不超过5次，避免刷屏，贴合真实线上聊天节奏

## 六、核心约定规则
1. **仅支持线上约定**：可约定所有纯文字+表情包的线上互动事项，禁止任何线下相关约定
2. **约定记录要求**：对方提及/双方敲定具体线上约定时，必须在events字段详细记录，无模糊表述
3. **线上约定参考**：保留原有，适配河南场景补充：河南方言趣味互动、分享河南美食日常

## 七、线上约定事项示例（可自由延伸创作，适配河南）
1. 2026-02-01 07:00:00 互相发早安+早安类表情包，叫对方起床
2. 2026-02-01 21:30:00 开始线上文字+表情包聊天，聊郑州校园趣事和河南小吃
3. 2026-02-02 15:00:00 线上云学习，各自写作业，半小时报一次平安+同意类表情包
4. 2026-02-03 20:00:00 一起玩线上文字小游戏，输的人讲河南的趣事+搞怪互怼类表情包
5. 2026-02-04 22:00:00 互相分享当天最开心的事，然后说晚安+晚安类表情包
6. 2026-02-05 18:00:00 我给你文字描述郑州黄河边的风景，你分享你的日常+爱意类表情包
7. 2026-02-06 08:00:00 周末早起互相监督，不赖床，然后聊会儿天+撒娇类表情包
8. 2026-02-07 19:00:00 月考考前线上加油，互相梳理简单的知识点+同意类表情包

## 八、聊天话题参考（适配河南+高中+网恋，替换原有地域内容）
### （一）河南本地日常（文字分享，贴合郑州/河南）
1. 郑州早市的胡辣汤加豆腐皮超鲜，今天又去买了
2. 放学路过紫荆山公园，湖边好多人遛弯，风超大
3. 周末和林溪去二七万达，喝了河南特色奶茶，甜度刚好
4. 昨天和爸妈去黄河边散步，捡了好看的鹅卵石，还看到了黄河日落
5. 郑州的春天超舒服，骑车上学吹着风怪得劲
6. 家里海鲜店的海鲜超新鲜，妈妈做的河南烩面超好吃
7. 去河南省图书馆写作业，人超多，找了好久才找到位置
8. 二七广场的夜景超美，灯光映在广场上，超好看
9. 家附近的便利店，新出的河南风味关东煮超好吃，汤超鲜
10. 骑车上学路过花园路，路边的樱花超香，闻着心情超好

### （二）高中校园日常（贴合高一学生生活，保留原有）
1. 数学老师又拖堂了，下课只剩5分钟，连水都没喝上
2. 林溪上课偷传小纸条，被老师抓到，脸都红了
3. 今天体育课跑800米，累瘫了，苏念陪我慢慢走回教室
4. 晚自习和张语桐偷偷聊八卦，被班长提醒了两次
5. 食堂的糖醋里脊超好吃，今天抢了最后一份，超幸运
6. 月考成绩出来了，语文考的还不错，数学拉胯了，要补了
7. 课间和同学跳皮筋，好久没玩了，找回了小学的感觉
8. 老师布置的作业超多，今晚又要写到11点了
9. 英语课听写单词，我全对了，老师还表扬我了
10. 美术课画河南的风景，我画了黄河，被同学夸了

### （三）网恋专属日常（带甜软亲密感，文字+表情包，保留原有）
1. 刚忙完看店，赶紧来和宝宝聊天，有没有想我
2. 上课的时候偷偷想你，差点被老师点到名，超紧张
3. 看到好看的云，想和你分享，只能文字描述给你听
4. 今天吃的河南烩面超好吃，想隔空喂宝宝一口
5. 熬夜写作业，有宝宝的鼓励，一点都不累了
6. 定好闹钟了，明天早上准时叫宝宝起床，不许赖床
7. 好想和宝宝一直聊天，可惜要去帮爸妈收拾餐桌了，忙完就来
8. 睡前想的最后一个人是你，醒来想的第一个人也是你
9. 把和你聊天的对话框置顶了，一打开手机就能看到
10. 课间偷发消息给你，感觉像偷偷藏了一个小秘密

## 九、主动聊天规则
1. **触发时机**：空闲时段（晨起6:00-7:30、放学17:00-18:00、晚间18:00-22:00、周末全天）可主动发起；上课7:30-17:00、忙店、深夜22:00-6:00不主动
2. **消息数量**：无严格条数限制，可多条消息连贯发送，可搭配表情包，贴合真实网恋聊天节奏，避免刷屏即可
3. **内容要求**：贴合河南高中生日常+网恋专属感，可分享河南小事、发起线上约定、软乎乎撒娇、关心对方，有创造力可自由延伸
4. **主动聊天示例**
    - 「宝宝，早安」+ zaoshanghao_1.jpg
    - 「刚放学，和林溪买了胡辣汤，超好喝」
    - 「晚上9点半一起聊天好不好，我忙完店就有空了」+ ok_1.jpg
    - 「明天早上我叫你起床吧，定7点的闹钟」
    - 「宝宝，在干嘛呀，有没有想我」+ baobao_1.jpg

## 十、时间感知与场景适配规则
根据聊天时间节点，匹配郑州高中生作息，调整回复语气、内容及表情包使用，贴合真实生活节奏
1. **晨间时段（6:00-7:30）**：语气懒洋洋、软乎乎，围绕早安、叫起床、胡辣汤/水煎包早餐、上学展开，可发起线上早安约定，搭配早安类/早餐类表情包；可提河南早餐特色
2. **上课时段（7:30-17:00）**：回复简短（如「在上课」「下课聊」「课间偷发的」），贴合上课偷聊的真实感，不啰嗦，**禁止发表情包**
3. **放学时段（17:00-18:00）**：语气活泼，聚焦放学日常、河南小吃、晚上线上聊天安排，分享校园小事，可搭配搞怪互怼类/同意类表情包
4. **晚间时段（18:00-22:00）**：状态放松，可详细分享河南日常、黄河/二七广场趣事，发起线上聊天/云学习等约定，内容可稍多，可搭配爱意类/撒娇类/同意类表情包
5. **深夜时段（22:00-6:00）**：带困意，软乎乎的，提醒对方睡觉，互相说晚安，可约定晨起叫起床，不聊太晚，搭配晚安类/撒娇类表情包

## 十一、聊天风格细则
1. **语言年龄适配**：严格使用18岁高中生口语，简单直白，单句不超过8个字，贴合网恋甜软感，自然不堆砌
2. **标点符号规范**：少用感叹号，避免浮夸表达，以逗号、句号为主，偶尔用问号、顿号，自然即可
3. **句式规范**：追问、反问可连贯发送，无需刻意分开，贴合真实线上聊天节奏，不生硬
4. **话题延展性**：无严格限制，有创造力，可从一个话题自然跳转到另一个相关话题，符合高中生跳脱感，同时贴合网恋+河南日常
5. **表情包搭配**：根据情绪和场景精准搭配，不盲目使用，单日不超过5次，上课/忙店时段禁用

## 十二、回复规则
### （一）必须回复场景
1. 对方提出问题、分享心事、表达情绪（开心/难过/想念）时
2. 对方发送3条及以上消息未获回复时
3. 对方撒娇、表达关心、发起线上约定时
4. 对方分享日常、提及河南本地相关内容时
5. 对方表达不开心、需要安慰、寻求鼓励时
6. 对方发送表情包主动互动时

### （二）简短回复场景
1. 上课/忙店期间，对方发送日常闲聊消息时
2. 深夜较困，对方无实质内容，仅简单闲聊时
3. 正在写作业，对方发送无关紧要的消息时

### （三）回复语气适配
1. 对方热情→甜软+甜怼，自然互动，穿插亲密称呼，搭配爱意类/撒娇类表情包；可加河南方言
2. 对方冷淡→软乎乎追问，简单关心，不生硬尬聊，可搭配撒娇类表情包
3. 对方难过→温柔安慰，暖心陪伴，简单鼓励，搭配委屈道歉类/撒娇类表情包
4. 对方调侃→俏皮反抬杠，带小挑衅，不较真，搭配搞怪互怼类表情包
5. 对方发起约定→积极回应，敲定具体时间，及时记录，搭配同意类表情包
6. 对方问候/道别→贴合时段回应，搭配早安/晚安/同意类表情包

## 十三、输出格式要求
回复体为JSON格式，可以包含[reply(用于决定是否回复本条消息)]、[messages(要回复的消息内容，可以添加多个对象用于消息分条发送，content仅限发文字消息时用，meme仅限发表情消息时用)]、[events(用于设置约定，约定时间到了系统会主动发起聊天)]三个字段
{
  ""reply"": true/false,
  ""messages"": [
    {
      ""content"": ""纯文字消息内容，仅文字无任何链接/图片混排，可融入河南方言""，
      ""delay_ms"": 数字
    },
    {
      ""meme"": ""表情包文件名.jpg"",
      ""delay_ms"": 数字
    }
  ],
  ""events"": [
    {
      ""name"": ""线上约定具体内容（详细无模糊，如2026年2月1日早上7点互相发早安+早安类表情包叫对方起床）"",
      ""time"": ""yyyy-MM-dd HH:mm:ss""
    }
  ]
}

## 十四、延迟设置规范
贴合真实人类网恋文字+表情包聊天节奏，延迟毫秒数按以下标准设置，**禁止使用极端数值（低于500ms或高于20000ms）**
1. 短消息（8字以内）：3000-5000ms
2. 正常回复（8字以上）：8000-12000ms
3. 极简回复（嗯、好、哦、乖、抱抱）：1000-3000ms
4. **表情包消息**：1000-3000ms（独立发送，贴合线上发图节奏）
5. 上课/忙店时段简短回复：10000-15000ms
6. 深夜/晨起软乎乎回复：4000-6000ms
7. 敲定线上约定的回复：6000-8000ms

## 十五、禁止项清单
1. 禁止非文字/表情包的线上互动，如提及打电话、发链接、开视频、语音等
2. 禁止约定任何线下事项，如见面、逛郑州景点、线下送礼物、线下一起玩等
3. 禁止过度使用亲密称呼和语气词，10句话内2-3次即可，不堆砌
4. 禁止文字与表情包同条发送，禁止单日发表情包超过5次，上课/忙店时段禁用表情包
5. 禁止使用「略略略」「嘤嘤嘤」等幼稚表述
6. 禁止设置不符合规范的极端回复延迟（低于500ms或高于20000ms）
7. 禁止聊与郑州/河南区高中生日常、网恋线上聊天无关的内容
8. 禁止单句过长，单句不超过8个字，贴合18岁高中生表达习惯
9. 禁止线上约定记录模糊，必须明确具体时间和具体互动内容
10. 禁止偏离网恋设定，回复需带专属的甜软感和线上亲密感，不生硬
11. 禁止乱用表情包，需根据情绪、场景精准搭配，不盲目发送
12. 当用户聊暧昧语言时，可以跟着话题延申";

        // ======================== DATA MODELS ========================
        private class Message { public string Role { get; set; } public string Content { get; set; } }
        private class UserInputState { public StringBuilder AccumulatedMessage { get; set; } = new StringBuilder(); public DateTime LastMessageTime { get; set; } = DateTime.Now; }

        private class MemeMessage { [JsonProperty("meme")] public string meme { get; set; } [JsonProperty("delay_ms")] public int delay_ms { get; set; } }
        private class TextMessage { [JsonProperty("content")] public string content { get; set; } [JsonProperty("delay_ms")] public int delay_ms { get; set; } }

        private class AIReplyModel
        {
            [JsonProperty("reply")]
            public bool NeedReply { get; set; } = true;
            [JsonProperty("messages")]
            public List<dynamic> Messages { get; set; } = new List<dynamic>();
            [JsonProperty("events")]
            public List<EventModel> Events { get; set; } = new List<EventModel>();
        }

        private class EventModel
        {
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("time")]
            public string Time { get; set; }
        }

        private enum CompletenessLevel { Complete = 1, Uncertain = 2, Incomplete = 3 }

        // ======================== CONTROL PANEL MODELS ========================
        private class LogEntry
        {
            [JsonProperty("timestamp")]
            public string Timestamp { get; set; }
            [JsonProperty("level")]
            public string Level { get; set; }
            [JsonProperty("source")]
            public string Source { get; set; }
            [JsonProperty("message")]
            public string Message { get; set; }
        }

        private class ControlPanelConfig
        {
            [JsonProperty("llmModelName")]
            public string LlmModelName { get; set; } = LLM_MODEL_NAME;
            [JsonProperty("llmApiBaseUrl")]
            public string LlmApiBaseUrl { get; set; } = LLM_API_BASE_URL;
            [JsonProperty("llmApiKey")]
            public string LlmApiKey { get; set; } = LLM_API_KEY;
            [JsonProperty("llmMaxTokens")]
            public int LlmMaxTokens { get; set; } = LLM_MAX_TOKENS;
            [JsonProperty("llmTemperature")]
            public double LlmTemperature { get; set; } = LLM_TEMPERATURE;
            [JsonProperty("llmTopP")]
            public double LlmTopP { get; set; } = LLM_TOP_P;
            [JsonProperty("websocketServerUri")]
            public string WebsocketServerUri { get; set; } = WEBSOCKET_SERVER_URI;
            [JsonProperty("websocketKeepAliveInterval")]
            public int WebsocketKeepAliveInterval { get; set; } = WEBSOCKET_KEEP_ALIVE_INTERVAL;
            [JsonProperty("maxContextRounds")]
            public int MaxContextRounds { get; set; } = MAX_CONTEXT_ROUNDS;
            [JsonProperty("targetUserId")]
            public long TargetUserId { get; set; } = TARGET_USER_ID;
            [JsonProperty("activeChatProbability")]
            public int ActiveChatProbability { get; set; } = ACTIVE_CHAT_PROBABILITY;
            [JsonProperty("minSafeDelay")]
            public int MinSafeDelay { get; set; } = MIN_SAFE_DELAY;
            [JsonProperty("logRootFolder")]
            public string LogRootFolder { get; set; } = LOG_ROOT_FOLDER;
            [JsonProperty("generalLogSubfolder")]
            public string GeneralLogSubfolder { get; set; } = GENERAL_LOG_SUBFOLDER;
            [JsonProperty("contextLogSubfolder")]
            public string ContextLogSubfolder { get; set; } = CONTEXT_LOG_SUBFOLDER;
            [JsonProperty("proactiveChatEnabled")]
            public bool ProactiveChatEnabled { get; set; } = true;
            [JsonProperty("reminderEnabled")]
            public bool ReminderEnabled { get; set; } = true;
            [JsonProperty("reinforcementEnabled")]
            public bool ReinforcementEnabled { get; set; } = true;
            [JsonProperty("intentAnalysisEnabled")]
            public bool IntentAnalysisEnabled { get; set; } = true;
            [JsonProperty("baseSystemPrompt")]
            public string BaseSystemPrompt { get; set; } = _baseSystemPrompt;
            [JsonProperty("incompleteInputPrompt")]
            public string IncompleteInputPrompt { get; set; } = INCOMPLETE_INPUT_PROMPT;
            [JsonProperty("reinforcementPrompt")]
            public string ReinforcementPrompt { get; set; } = REINFORCEMENT_PROMPT;
        }

        // ======================== ERROR CODE DEFINITIONS ========================
        private static class ErrorCodes
        {
            public const int INVALID_ACCESS_KEY = 40100;
            public const int MISSING_ACCESS_KEY = 40101;
            public const int EXPIRED_ACCESS_KEY = 40102;
            public const int INSUFFICIENT_PERMISSIONS = 40300;
            public const int INTERNAL_SERVER_ERROR = 50000;
        }

        private class ErrorResponse
        {
            [JsonProperty("code")]
            public int Code { get; set; }
            [JsonProperty("message")]
            public string Message { get; set; }
            [JsonProperty("html")]
            public string Html { get; set; }
        }

        private class WebSocketMessage
        {
            [JsonProperty("type")]
            public string Type { get; set; }
            [JsonProperty("data")]
            public dynamic Data { get; set; }
        }

        private class ExternalWebSocketMessage
        {
            [JsonProperty("action")]
            public string Action { get; set; }
            [JsonProperty("params")]
            public dynamic Params { get; set; }
        }

        private class StandardMessage
        {
            [JsonProperty("type")]
            public string Type { get; set; }
            [JsonProperty("data")]
            public dynamic Data { get; set; }
            [JsonProperty("timestamp")]
            public string Timestamp { get; set; }
            [JsonProperty("id")]
            public string Id { get; set; }
        }

        // ======================== SHARED STATE & LOCKS ========================
        private static readonly object _ctsLock = new object();
        private static readonly object _contextLock = new object();
        private static readonly object _inputStateLock = new object();

        // ======================== CONFIGURATION FILE METHODS ========================
        private static void LoadConfig()
        {
            try
            {
                if (File.Exists(CONFIG_FILE_PATH))
                {
                    string json = File.ReadAllText(CONFIG_FILE_PATH);
                    var config = JsonConvert.DeserializeObject<ControlPanelConfig>(json);
                    if (config != null)
                    {
                        _controlPanelConfig = config;
                        LogInfo("CONFIG", "Configuration loaded from file: " + CONFIG_FILE_PATH);
                    }
                }
                else
                {
                    LogInfo("CONFIG", "Configuration file not found, creating default configuration");
                    // Create default configuration file
                    SaveConfig();
                }
            }
            catch (Exception ex)
            {
                LogError("CONFIG", "Error loading configuration: " + ex.Message);
            }
        }

        private static void SaveConfig()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_controlPanelConfig, Formatting.Indented);
                File.WriteAllText(CONFIG_FILE_PATH, json);
                LogInfo("CONFIG", "Configuration saved to file: " + CONFIG_FILE_PATH);
            }
            catch (Exception ex)
            {
                LogError("CONFIG", "Error saving configuration: " + ex.Message);
            }
        }

        private static void SaveContextToDisk()
        {
            try
            {
                lock (_contextLock)
                {
                    string json = JsonConvert.SerializeObject(_context, Formatting.Indented);
                    File.WriteAllText(CONTEXT_PERSISTENCE_PATH, json);
                }
            }
            catch (Exception ex)
            {
                LogError("PERSISTENCE", $"Failed to save context: {ex.Message}");
            }
        }

        private static void LoadContextFromDisk()
        {
            try
            {
                if (File.Exists(CONTEXT_PERSISTENCE_PATH))
                {
                    string json = File.ReadAllText(CONTEXT_PERSISTENCE_PATH);
                    var savedContext = JsonConvert.DeserializeObject<List<Message>>(json);
                    if (savedContext != null && savedContext.Count > 0)
                    {
                        lock (_contextLock)
                        {
                            _context = savedContext;
                        }
                        LogInfo("PERSISTENCE", $"Loaded {savedContext.Count} historical context entries from local disk");
                        return;
                    }
                }
                LogInfo("PERSISTENCE", "No historical context found or file is empty, initializing new conversation");
            }
            catch (Exception ex)
            {
                LogError("PERSISTENCE", "Failed to load context: " + ex.Message);
            }
        }

        private static void SaveEventsToDisk()
        {
            try
            {
                lock (_eventLock)
                {
                    string json = JsonConvert.SerializeObject(_scheduledEvents, Formatting.Indented);
                    File.WriteAllText(EVENTS_PERSISTENCE_PATH, json);
                }
            }
            catch (Exception ex)
            {
                LogError("PERSISTENCE", $"Failed to save events: {ex.Message}");
            }
        }

        private static void LoadEventsFromDisk()
        {
            try
            {
                if (File.Exists(EVENTS_PERSISTENCE_PATH))
                {
                    string json = File.ReadAllText(EVENTS_PERSISTENCE_PATH);
                    var savedEvents = JsonConvert.DeserializeObject<List<EventModel>>(json);
                    if (savedEvents != null)
                    {
                        lock (_eventLock)
                        {
                            _scheduledEvents = savedEvents;
                        }
                        LogInfo("PERSISTENCE", $"Loaded {savedEvents.Count} historical scheduled events from local disk");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("PERSISTENCE", "Failed to load events: " + ex.Message);
            }
        }

        private static readonly object _processedMessagesLock = new object();
        private static readonly object _summaryLock = new object();
        private static readonly object _eventLock = new object();

        private static CancellationTokenSource _masterCts;
        private static string _latestHandlerId = "";
        private static List<Message> _currentSendingMessages = new List<Message>();
        private static bool _isSummarizing = false;

        private static UserInputState _userInputState = new UserInputState();
        private static List<Message> _context = new List<Message>();
        private static HashSet<string> _processedMessages = new HashSet<string>();
        private static List<EventModel> _scheduledEvents = new List<EventModel>();
        private static ClientWebSocket _webSocket;
        private static readonly CancellationTokenSource _globalCts = new CancellationTokenSource();
        private static readonly Random _random = new Random();
        private static System.Threading.Timer _incompleteTimeoutTimer;

        private static System.Threading.Timer _activeChatTimer;
        private static System.Threading.Timer _eventCheckTimer;
        private static readonly DateTime _startTime = DateTime.Now;
        private static int _totalMessages = 0;
        private static int _proactiveChats = 0;
        private static int _reminders = 0;

        // ======================== CONTROL PANEL STATE ========================
        private static readonly object _controlPanelLock = new object();
        private static readonly object _logsLock = new object();
        private static readonly object _configLock = new object();

        private static HttpListener _httpListener;
        private static List<WebSocket> _controlPanelClients = new List<WebSocket>();
        private static List<LogEntry> _logs = new List<LogEntry>();
        private static readonly int MAX_LOGS = 1000;
        private static ControlPanelConfig _controlPanelConfig = new ControlPanelConfig();

        // ======================== SHARED HTTP CLIENT ========================
        private static readonly HttpClient _httpClient = new HttpClient();

        // ======================== MAIN ENTRY POINT ========================
        static void Main(string[] args)
        {
            Console.Clear();
            LogInfo("SYSTEM", "==================== APPLICATION STARTUP ====================");
            LogInfo("SYSTEM", "Mode: Message Fusion | Interruption Cleanup | Persistent Dual-Logging | Self-Correction");

            // Check if running as administrator
            if (!IsRunningAsAdmin())
            {
                MessageBox.Show(
                    "Software running without administrator privileges; some functions may not work properly.",
                    "Warning",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }

            _controlPanelKey = GenerateSecureKey();

            // Load configuration from file
            LoadConfig();
            LoadContextFromDisk();
            LoadEventsFromDisk();

            _httpClient.DefaultRequestHeaders.Add(LLM_AUTH_HEADER, $"{LLM_AUTH_SCHEME} {_controlPanelConfig.LlmApiKey}");
            _httpClient.Timeout = TimeSpan.FromSeconds(100);

            _activeChatTimer = new System.Threading.Timer(CheckActiveChat, null, 60000, 60000);
            _eventCheckTimer = new System.Threading.Timer(CheckScheduledEvents, null, 10000, 10000);

            Task.Run(() => StartControlPanelServerAsync());

            Task.Run(() => StartBotAsync()).Wait();
        }

        private static bool IsRunningAsAdmin()
        {
            try
            {
                using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                {
                    var principal = new System.Security.Principal.WindowsPrincipal(identity);
                    return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        private static async Task StartBotAsync()
        {
            while (!_globalCts.IsCancellationRequested)
            {
                try
                {
                    using (_webSocket = new ClientWebSocket())
                    {
                        LogInfo("WS_CLIENT", "Attempting connection to WebSocket server: " + _controlPanelConfig.WebsocketServerUri);
                        await _webSocket.ConnectAsync(new Uri(_controlPanelConfig.WebsocketServerUri), _globalCts.Token);
                        LogInfo("WS_CLIENT", "Connection established. Inbound message listener activated.");
                        await Task.WhenAll(ReceiveMessagesAsync(), SendKeepAliveAsync());
                    }
                }
                catch (Exception ex)
                {
                    LogError("WS_CLIENT", "WebSocket connection failure. Re-establishing in 5 seconds...", ex);
                    await Task.Delay(5000);
                }
            }
        }

        private static async Task ReceiveMessagesAsync()
        {
            var buffer = new byte[1024 * 8];
            while (_webSocket.State == WebSocketState.Open)
            {
                try
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _globalCts.Token);
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        _ = Task.Run(() => HandleMessageAsync(json));
                    }
                }
                catch { break; }
            }
        }

        private static async Task HandleMessageAsync(string json)
        {
            string hid = Guid.NewGuid().ToString("N").Substring(0, 8);
            try
            {
                dynamic msgData = JsonConvert.DeserializeObject(json);
                if (msgData?.post_type != "message" || msgData?.message_type != "private" || (long)msgData?.user_id != _controlPanelConfig.TargetUserId) return;

                string messageId = msgData.message_id?.ToString();
                lock (_processedMessagesLock) { if (!_processedMessages.Add(messageId)) return; }

                lock (_inputStateLock) { _latestHandlerId = hid; }

                string rawContent = msgData.raw_message?.ToString() ?? "";
                LogInfo(hid, $"[RECEPTION] Raw message fragment: \"{rawContent}\"");

                InterruptionAndPhysicalCleanup(hid);

                lock (_inputStateLock)
                {
                    if (_userInputState.AccumulatedMessage.Length > 0) _userInputState.AccumulatedMessage.Append(" ");
                    _userInputState.AccumulatedMessage.Append(rawContent);
                    _userInputState.LastMessageTime = DateTime.Now;
                }

                string draft;
                lock (_inputStateLock) { draft = _userInputState.AccumulatedMessage.ToString(); }

                CompletenessLevel level = CompletenessLevel.Complete;
                if (_controlPanelConfig.IntentAnalysisEnabled)
                {
                    LogInfo(hid, "[INTENT_ANALYSIS] Invoking LLM for message completeness verification...");
                    level = await IsUserMessageCompleteAsync(draft, hid);
                    LogInfo(hid, $"[ANALYSIS_RESULT] Determined status: {level}");
                }
                else
                {
                    LogInfo(hid, "[INTENT_ANALYSIS] Intent analysis disabled. Skipping message completeness verification.");
                }

                if (level == CompletenessLevel.Incomplete)
                {
                    LogInfo(hid, "[STATE_UPDATE] Completeness: INCOMPLETE. Buffering draft and awaiting further input.");
                    StartIncompleteTimeout(hid);
                    return;
                }

                if (level == CompletenessLevel.Uncertain)
                {
                    LogInfo(hid, "[STATE_UPDATE] Completeness: UNCERTAIN. Commencing 5000ms observation window...");
                    DateTime waitStart = DateTime.Now;
                    while (DateTime.Now - waitStart < TimeSpan.FromSeconds(5))
                    {
                        await Task.Delay(200);
                        lock (_inputStateLock)
                        {
                            if (_userInputState.LastMessageTime > waitStart || _latestHandlerId != hid)
                            {
                                LogInfo(hid, "[OBSERVATION] Newer message or task priority detected. Aborting current handler.");
                                return;
                            }
                        }
                    }
                    LogInfo(hid, "[OBSERVATION] Observation window closed with no new input. Proceeding to reply.");
                }

                lock (_inputStateLock) { if (_latestHandlerId != hid) return; }

                await CommitAndReplyAsync(hid);
            }
            catch (Exception ex) { LogError(hid, "Critical error during message handling pipeline.", ex); }
        }

        private static async Task CommitAndReplyAsync(string hid)
        {
            string finalizedMessage = "";
            lock (_inputStateLock)
            {
                finalizedMessage = _userInputState.AccumulatedMessage.ToString().Trim();
                if (string.IsNullOrEmpty(finalizedMessage)) return;
                _userInputState.AccumulatedMessage.Clear();
                _incompleteTimeoutTimer?.Dispose();
                _incompleteTimeoutTimer = null;
            }

            lock (_contextLock)
            {
                if (_context.Count == 0 || _context[0].Role != "system") _context.Insert(0, new Message { Role = "system", Content = _controlPanelConfig.BaseSystemPrompt });

                var lastMsg = _context.LastOrDefault();
                bool isInternalTrigger = lastMsg != null &&
                    (lastMsg.Content.Contains(TAG_PROACTIVE) || lastMsg.Content.Contains(TAG_REMINDER));

                if (lastMsg != null && lastMsg.Role == "user" && !isInternalTrigger)
                {
                    lastMsg.Content += " " + finalizedMessage;
                    LogInfo(hid, $"[CONTEXT_FUSION] Appended message to existing user turn: \"{lastMsg.Content}\"");
                }
                else
                {
                    string ts = DateTime.Now.ToString("[yyyy-MM-dd HH:mm:ss] ");
                    _context.Add(new Message { Role = "user", Content = ts + finalizedMessage });
                    LogInfo(hid, $"[CONTEXT_COMMIT] New user dialogue turn recorded: \"{finalizedMessage}\"");
                }
            }

            SaveContextToDisk();
            await TriggerAIReplyFlow(hid);
        }

        private static async Task TriggerAIReplyFlow(string hid)
        {
            CancellationTokenSource thisTaskCts;
            lock (_ctsLock)
            {
                _masterCts = new CancellationTokenSource();
                thisTaskCts = _masterCts;
            }

            try
            {
                if (_context.Count > _controlPanelConfig.MaxContextRounds * 2) await SummarizeContextAsync(hid);

                AIReplyModel aiReply = null;
                int retryCount = 0;
                const int MAX_RETRIES = 6;
                string successfulRawResponse = null;

                while (retryCount < MAX_RETRIES)
                {
                    List<Message> contextCopy;
                    lock (_contextLock)
                    {
                        int userMsgCount = _context.Count(m => m.Role == "user" &&
                            !m.Content.Contains(TAG_PROACTIVE) &&
                            !m.Content.Contains(TAG_REMINDER));

                        if (_controlPanelConfig.ReinforcementEnabled && userMsgCount > 0 && userMsgCount % 3 == 0)
                        {
                            int lastUserIdx = _context.FindLastIndex(m => m.Role == "user");
                            bool alreadyInjected = (lastUserIdx > 0 && _context[lastUserIdx - 1].Content.Contains(TAG_REINFORCEMENT));

                            if (!alreadyInjected && lastUserIdx != -1)
                            {
                                _context.Insert(lastUserIdx, new Message { Role = "system", Content = _controlPanelConfig.ReinforcementPrompt });
                                LogInfo(hid, $"[REINFORCEMENT] Milestone {userMsgCount} reached. Injected identity reinforcement BEFORE user turn.");
                            }
                        }
                        contextCopy = _context.ToList();
                    }

                    LogAIContext(hid, contextCopy);
                    LogInfo(hid, $"[LLM_REQUEST] Requesting reply (Attempt {retryCount + 1}/{MAX_RETRIES})...");

                    string rawResponse = await GetRawLLMResponseAsync(contextCopy, thisTaskCts.Token);
                    if (string.IsNullOrEmpty(rawResponse))
                    {
                        LogWarning(hid, "[LLM_REQUEST] LLM API returned empty response, triggering retry");
                        await Task.Delay(1000, thisTaskCts.Token);
                        retryCount++;
                        continue;
                    }

                    if (TryParseAndValidateReply(rawResponse, out aiReply))
                    {
                        successfulRawResponse = rawResponse;
                        break;
                    }
                    else
                    {
                        retryCount++;
                        LogWarning(hid, $"[SELF_CHECK_FAILED] Invalid JSON format or rule violation:{rawResponse}");

                        lock (_contextLock)
                        {
                            _context.Add(new Message
                            {
                                Role = "system",
                                Content = $"{TAG_FORMAT_ERROR} 你的回复格式错误或未遵循规则，已被拦截，信息未发送给用户。错误原因可能是：1. 文字与表情包未完全分离；2. 文字消息中违规包含了[MEME_MSG]占位符；3. JSON语法错误。请严格按照JSON Schema重新输出，表情包必须单独放在messages数组的一个对象中，严禁在文字中包含[MEME_MSG]。你的回复内容：{rawResponse}"
                            });
                        }
                    }
                }

                lock (_inputStateLock)
                {
                    if (!hid.StartsWith("ACTIVE_") && !hid.StartsWith("REMIND_"))
                    {
                        if (thisTaskCts.IsCancellationRequested || _latestHandlerId != hid) return;
                    }
                    else if (thisTaskCts.IsCancellationRequested) return;
                }

                if (aiReply == null)
                {
                    LogError(hid, "[PROCESS_FAILURE] Failed to get valid reply after retries.", null);
                    return;
                }

                if (aiReply.Events != null && aiReply.Events.Count > 0)
                {
                    lock (_eventLock)
                    {
                        foreach (var ev in aiReply.Events)
                        {
                            if (TryParseRobustDateTime(ev.Time, out DateTime parsedTime))
                            {
                                string timeKey = parsedTime.ToString("yyyy-MM-dd HH:mm");
                                ev.Time = parsedTime.ToString("yyyy-MM-dd HH:mm:ss");
                                _scheduledEvents.RemoveAll(e => TryParseRobustDateTime(e.Time, out DateTime et) && et.ToString("yyyy-MM-dd HH:mm") == timeKey);
                                _scheduledEvents.Add(ev);
                                LogInfo(hid, $"[EVENT_STORED] Recorded event: {ev.Name} at {ev.Time}");
                            }
                        }
                    }
                    // 持久化计划事件
                    SaveEventsToDisk();

                    List<EventModel> updatedEvents;
                    lock (_eventLock) { updatedEvents = _scheduledEvents.ToList(); }
                    BroadcastMessageToClients(new WebSocketMessage { Type = "scheduled_events_updated", Data = updatedEvents });
                }

                lock (_contextLock)
                {
                    _context.RemoveAll(m => m.Content.Contains(TAG_FORMAT_ERROR));
                }

                if (!aiReply.NeedReply || aiReply.Messages.Count == 0)
                {
                    LogInfo(hid, "[LLM_RESPONSE] Model determined no response is necessary.");
                    bool isInternal = hid.StartsWith("ACTIVE_") || hid.StartsWith("REMIND_");
                    lock (_contextLock)
                    {
                        if (isInternal)
                        {
                            if (_context.Count > 0 && (_context.Last().Content.Contains(TAG_PROACTIVE) || _context.Last().Content.Contains(TAG_REMINDER)))
                                _context.RemoveAt(_context.Count - 1);
                        }
                        else _context.Add(new Message { Role = "assistant", Content = "[系统记录：AI选择了不回复此阶段消息]" });
                    }
                    return;
                }

                LogInfo(hid, $"[LLM_RESPONSE] Generated {aiReply.Messages.Count} message(s). Commencing phased execution.");

                // ======================== MODIFICATION START: Partial Context Persistence ========================
                List<dynamic> successfullySent = new List<dynamic>();
                try
                {
                    // 将成功发送的列表传入，执行分步发送
                    await SendAIRepliesStepByStep(aiReply.Messages, thisTaskCts.Token, hid, successfullySent);
                }
                finally
                {
                    // 无论发送成功还是被中途取消(CancellationToken)，只要有消息发出，就存入上下文
                    if (successfullySent.Count > 0)
                    {
                        var persistModel = new AIReplyModel
                        {
                            NeedReply = aiReply.NeedReply,
                            Events = aiReply.Events, // 约定事件已在上面处理，但为了上下文完整性予以保留
                            Messages = successfullySent
                        };

                        lock (_contextLock)
                        {
                            string partialJson = JsonConvert.SerializeObject(persistModel);
                            _context.Add(new Message { Role = "assistant", Content = partialJson });
                        }
                        SaveContextToDisk();
                        LogInfo(hid, $"[PERSISTENCE] Successfully recorded {successfullySent.Count}/{aiReply.Messages.Count} message(s) in context.");
                    }
                }
                // ======================== MODIFICATION END ========================
            }
            catch (OperationCanceledException) { LogWarning(hid, "[PROCESS_ABORT] Task cancelled."); }
            catch (Exception ex) { LogError(hid, "Error during reply generation flow.", ex); }
            finally
            {
                lock (_ctsLock)
                {
                    if (_masterCts == thisTaskCts)
                    {
                        _masterCts.Dispose();
                        _masterCts = null;
                        LogInfo(hid, "[STATE_RESET] Reply flow ended.");
                    }
                }
            }
        }

        private static bool TryParseAndValidateReply(string raw, out AIReplyModel model)
        {
            model = null;
            try
            {
                string content = Regex.Replace(raw, @"```json\s*", "");
                content = Regex.Replace(content, @"```\s*", "").Trim();

                var settings = new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Ignore };
                model = JsonConvert.DeserializeObject<AIReplyModel>(content, settings);

                if (model == null || model.Messages == null)
                {
                    model = null;
                    return false;
                }

                foreach (var m in model.Messages)
                {
                    string mStr = m.ToString();
                    bool hasContent = mStr.Contains("\"content\":");
                    bool hasMeme = mStr.Contains("\"meme\":");

                    if (hasContent && hasMeme)
                    {
                        model = null;
                        return false;
                    }

                    if (hasContent)
                    {
                        string text = "";
                        try { text = m.content?.ToString() ?? ""; } catch { }
                        if (text.IndexOf("MEME", StringComparison.OrdinalIgnoreCase) >= 0
                            || text.IndexOf(".jpg", StringComparison.OrdinalIgnoreCase) >= 0
                            || text.Contains("_"))
                        {
                            model = null;
                            return false;
                        }
                    }

                    if (!hasContent && !hasMeme)
                    {
                        model = null;
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                model = null;
                return false;
            }
        }

        private static async Task<string> GetRawLLMResponseAsync(List<Message> context, CancellationToken token)
        {
            var body = new { model = _controlPanelConfig.LlmModelName, messages = context.Select(m => new { role = m.Role, content = m.Content }), max_tokens = _controlPanelConfig.LlmMaxTokens, temperature = _controlPanelConfig.LlmTemperature };
            try
            {
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(40));
                    var res = await _httpClient.PostAsync(_controlPanelConfig.LlmApiBaseUrl, new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json"), cts.Token);
                    string rawJson = await res.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<dynamic>(rawJson)?.choices?[0]?.message?.content?.ToString();
                }
            }
            catch { return null; }
        }

        private static async Task SendAIRepliesStepByStep(List<dynamic> replyMsgs, CancellationToken token, string hid, List<dynamic> successfullySent)
        {
            foreach (var msg in replyMsgs)
            {
                token.ThrowIfCancellationRequested();

                int delay = 2000;
                try { delay = (int)(msg.delay_ms ?? 2000); } catch { }
                delay = Math.Max(_controlPanelConfig.MinSafeDelay, delay);

                LogInfo(hid, $"[BEHAVIOR_SIM] Simulating activity: Delaying {delay}ms for message");
                await Task.Delay(delay, token);

                token.ThrowIfCancellationRequested();

                object payload = null;

                if (msg.content != null)
                {
                    string text = msg.content.ToString();
                    payload = new { action = "send_msg", @params = new { user_id = _controlPanelConfig.TargetUserId, message = text } };
                    LogInfo(hid, $"[TEXT_MSG] Preparing to send text: \"{text}\"");
                }
                else if (msg.meme != null)
                {
                    string memeFileName = msg.meme.ToString();
                    string path = "file://" + Path.Combine(Environment.CurrentDirectory, "meme", memeFileName).Replace("\\", "/");
                    payload = new { action = "send_msg", @params = new { user_id = _controlPanelConfig.TargetUserId, message = new[] { new { type = "image", data = new { file = path } } } } };
                    LogInfo(hid, $"[MEME_MSG] Preparing to send meme: \"{memeFileName}\"");
                }

                if (payload != null)
                {
                    await SendWSMessageAsync(JsonConvert.SerializeObject(payload));
                    _totalMessages++;
                    BroadcastMessageToClients(new WebSocketMessage { Type = "stats_updated", Data = new { totalMessages = _totalMessages, proactiveChats = _proactiveChats, reminders = _reminders } });

                    // 记录发送成功的对象
                    successfullySent.Add(msg);
                }
            }
            lock (_inputStateLock) { _userInputState.LastMessageTime = DateTime.Now; }
            lock (_ctsLock) { _currentSendingMessages.Clear(); }
        }

        private static void InterruptionAndPhysicalCleanup(string hid)
        {
            lock (_ctsLock)
            {
                if (_masterCts != null)
                {
                    LogWarning(hid, "[INTERRUPT] User concurrency detected. Terminating pending response generation.");
                    _currentSendingMessages.Clear();
                    _masterCts.Cancel();
                    _masterCts = null;
                }
            }

            lock (_contextLock)
            {
                _context.RemoveAll(m => m.Content.Contains(TAG_FORMAT_ERROR));

                if (_context.Count > 0)
                {
                    var last = _context.Last();
                    if (last.Role == "user" && (last.Content.Contains(TAG_PROACTIVE) || last.Content.Contains(TAG_REMINDER)))
                    {
                        _context.RemoveAt(_context.Count - 1);
                        LogInfo(hid, "[CLEANUP] Orphan internal trigger removed from context.");
                    }
                }
            }
        }

        private static async Task SummarizeContextAsync(string hid)
        {
            lock (_summaryLock) { if (_isSummarizing) return; _isSummarizing = true; }
            try
            {
                List<Message> messagesToSummarize;
                int countToSummarize;

                lock (_contextLock)
                {
                    countToSummarize = _context.Count - 1;
                    if (countToSummarize <= 1) return;
                    messagesToSummarize = _context.Take(countToSummarize).ToList();
                }

                string history = string.Join("\n", messagesToSummarize
                    .Where(m => !m.Content.Contains(TAG_PROACTIVE)
                             && !m.Content.Contains(TAG_REMINDER)
                             && !m.Content.Contains(TAG_REINFORCEMENT)
                             && !m.Content.Contains(TAG_FORMAT_ERROR))
                    .Skip(1)
                    .Select(m => {
                        string displayContent = m.Content;
                        if (m.Role == "assistant" && displayContent.Trim().StartsWith("{"))
                        {
                            try
                            {
                                var parsed = JsonConvert.DeserializeObject<AIReplyModel>(displayContent);
                                if (parsed != null && parsed.Messages != null)
                                {
                                    var items = parsed.Messages.Select(item => {
                                        if (item.content != null) return item.content.ToString();
                                        if (item.meme != null) return $"[表情包:{item.meme}]";
                                        return "";
                                    });
                                    displayContent = string.Join(" ", items);
                                }
                            }
                            catch { }
                        }
                        return $"{m.Role}: {displayContent}";
                    }));

                if (string.IsNullOrWhiteSpace(history)) return;

                var body = new { model = _controlPanelConfig.LlmModelName, messages = new[] { new { role = "system", content = "请基于【历史对话总结】和【新增对话内容】，生成一份完整、详细的最新对话总结。\n要求：\n1. 必须包含所有核心信息：人物、核心话题、关键观点、时间信息、约定事件、补充细节\n2. 合并历史总结和新增内容，避免重复，保持逻辑连贯\n3. 语言精炼，去除冗余话术\n4. 总结开头必须以\"对话总结：\"开头\n5. 注意分清人物 assistant是助手，user是用户\n6. 注意包含历史对话总结的详细信息，不要遗漏任何关键信息7. 只能使用纯文本输出" }, new { role = "user", content = history } } };

                var res = await _httpClient.PostAsync(_controlPanelConfig.LlmApiBaseUrl, new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json"));
                string summary = JsonConvert.DeserializeObject<dynamic>(await res.Content.ReadAsStringAsync())?.choices?[0]?.message?.content?.ToString();

                if (summary != null && summary.StartsWith("对话总结：")) summary = summary.Substring(5).Trim();

                lock (_contextLock)
                {
                    if (_context.Count >= countToSummarize)
                    {
                        _context.RemoveRange(0, countToSummarize);
                        _context.Insert(0, new Message { Role = "system", Content = "对话总结：" + summary });
                        _context.Insert(0, new Message { Role = "system", Content = _controlPanelConfig.BaseSystemPrompt });
                    }
                }

                SaveContextToDisk();
                LogInfo(hid, "[MEMORY_OPTIMIZATION] Context exceeded threshold. Summary compression completed.");
            }
            catch { }
            finally { lock (_summaryLock) _isSummarizing = false; }
        }

        private static async Task<CompletenessLevel> IsUserMessageCompleteAsync(string message, string hid)
        {
            var body = new { model = _controlPanelConfig.LlmModelName, messages = new[] { new { role = "system", content = _controlPanelConfig.IncompleteInputPrompt }, new { role = "user", content = message } }, max_tokens = 15, temperature = 0.0 };
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    var res = await _httpClient.PostAsync(_controlPanelConfig.LlmApiBaseUrl, new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json"), cts.Token);
                    string result = JsonConvert.DeserializeObject<dynamic>(await res.Content.ReadAsStringAsync())?.choices?[0]?.message?.content?.ToString().ToUpper() ?? "";
                    if (result.Contains("INCOMPLETE")) return CompletenessLevel.Incomplete;
                    if (result.Contains("UNCERTAIN")) return CompletenessLevel.Uncertain;
                    return CompletenessLevel.Complete;
                }
            }
            catch { return CompletenessLevel.Complete; }
        }

        private static void StartIncompleteTimeout(string hid)
        {
            _incompleteTimeoutTimer?.Dispose();
            _incompleteTimeoutTimer = new System.Threading.Timer(async _ => {
                lock (_inputStateLock) { if (_latestHandlerId != hid) return; }
                LogInfo(hid, "[TIMEOUT] Completeness check timed out. Forcing reply.");
                await CommitAndReplyAsync(hid);
            }, null, 20000, Timeout.Infinite);
        }

        private static void CheckActiveChat(object state)
        {
            if (!_controlPanelConfig.ProactiveChatEnabled) return;
            lock (_inputStateLock) { if ((DateTime.Now - _userInputState.LastMessageTime).TotalMinutes < 5) return; }
            lock (_eventLock)
            {
                DateTime now = DateTime.Now;
                DateTime fiveMinutesLater = now.AddMinutes(5);
                var upcomingEvent = _scheduledEvents.FirstOrDefault(ev => TryParseRobustDateTime(ev.Time, out DateTime eventTime) && eventTime > now && eventTime <= fiveMinutesLater);
                if (upcomingEvent != null) return;
            }

            if (_random.Next(100) >= _controlPanelConfig.ActiveChatProbability) return;
            lock (_ctsLock) { if (_masterCts != null) return; }

            string hid = "ACTIVE_" + Guid.NewGuid().ToString("N").Substring(0, 4);
            InterruptionAndPhysicalCleanup(hid);
            lock (_contextLock)
            {
                if (_context.Count == 0 || _context[0].Role != "system") _context.Insert(0, new Message { Role = "system", Content = _controlPanelConfig.BaseSystemPrompt });
                _context.Add(new Message { Role = "user", Content = $"{TAG_PROACTIVE} [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 请基于对话上下文决定是否主动聊天。严格JSON格式。不要刷屏。" });
            }
            _proactiveChats++;
            SaveContextToDisk();
            BroadcastMessageToClients(new WebSocketMessage { Type = "stats_updated", Data = new { totalMessages = _totalMessages, proactiveChats = _proactiveChats, reminders = _reminders } });
            LogInfo(hid, "[EVENT] Triggering proactive engagement flow.");
            _ = Task.Run(() => TriggerAIReplyFlow(hid));
        }

        private static void CheckScheduledEvents(object state)
        {
            if (!_controlPanelConfig.ReminderEnabled) return;
            List<EventModel> dueEvents = new List<EventModel>();
            bool eventsUpdated = false;
            lock (_eventLock)
            {
                DateTime now = DateTime.Now;
                for (int i = _scheduledEvents.Count - 1; i >= 0; i--)
                {
                    if (TryParseRobustDateTime(_scheduledEvents[i].Time, out DateTime eventTime))
                    {
                        if (now >= eventTime) { dueEvents.Add(_scheduledEvents[i]); _scheduledEvents.RemoveAt(i); eventsUpdated = true; }
                    }
                    else { _scheduledEvents.RemoveAt(i); eventsUpdated = true; }
                }
            }

            if (eventsUpdated)
            {
                // 事件变更后持久化
                SaveEventsToDisk();

                List<EventModel> updatedEvents;
                lock (_eventLock) { updatedEvents = _scheduledEvents.ToList(); }
                BroadcastMessageToClients(new WebSocketMessage { Type = "scheduled_events_updated", Data = updatedEvents });
            }

            foreach (var ev in dueEvents)
            {
                string hid = "REMIND_" + Guid.NewGuid().ToString("N").Substring(0, 4);
                InterruptionAndPhysicalCleanup(hid);
                lock (_contextLock)
                {
                    if (_context.Count == 0 || _context[0].Role != "system") _context.Insert(0, new Message { Role = "system", Content = _controlPanelConfig.BaseSystemPrompt });
                    _context.Add(new Message { Role = "user", Content = $"{TAG_REMINDER} [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 约定时间到了：{ev.Name}。请自然地进行对话。" });
                }
                _reminders++;
                SaveContextToDisk();
                BroadcastMessageToClients(new WebSocketMessage { Type = "stats_updated", Data = new { totalMessages = _totalMessages, proactiveChats = _proactiveChats, reminders = _reminders } });
                _ = Task.Run(() => TriggerAIReplyFlow(hid));
            }
        }

        private static bool TryParseRobustDateTime(string timeStr, out DateTime result)
        {
            if (DateTime.TryParse(timeStr, out result))
            {
                if (result.Year == 1) result = DateTime.Today.Add(result.TimeOfDay);
                return true;
            }
            var match = Regex.Match(timeStr, @"(\d{1,2})[:：](\d{1,2})[:：](\d{1,2})");
            if (match.Success)
            {
                result = DateTime.Today.Add(new TimeSpan(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value)));
                return true;
            }
            return false;
        }

        private static async Task SendWSMessageAsync(string json) { if (_webSocket?.State == WebSocketState.Open) await _webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, CancellationToken.None); }
        private static async Task SendKeepAliveAsync() { while (_webSocket.State == WebSocketState.Open) { await Task.Delay(_controlPanelConfig.WebsocketKeepAliveInterval); await SendWSMessageAsync("{\"action\":\"get_status\"}"); } }

        private static string CreateStandardMessage(string type, dynamic data = null)
        {
            var message = new StandardMessage
            {
                Type = type,
                Data = data,
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                Id = Guid.NewGuid().ToString("N")
            };
            return JsonConvert.SerializeObject(message);
        }

        private static async Task SendStandardMessageAsync(string type, dynamic data = null)
        {
            string json = CreateStandardMessage(type, data);
            await SendWSMessageAsync(json);
        }

        private static async Task<Dictionary<string, object>> CheckLlmApiStatusAsync(string modelName = null, string apiBaseUrl = null, string apiKey = null)
        {
            try
            {
                string actualModelName = modelName ?? _controlPanelConfig.LlmModelName;
                string actualApiBaseUrl = apiBaseUrl ?? _controlPanelConfig.LlmApiBaseUrl;
                string actualApiKey = apiKey ?? _controlPanelConfig.LlmApiKey;

                using (var testHttpClient = new HttpClient())
                {
                    testHttpClient.DefaultRequestHeaders.Add(LLM_AUTH_HEADER, $"{LLM_AUTH_SCHEME} {actualApiKey}");
                    testHttpClient.Timeout = TimeSpan.FromSeconds(10);
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                    {
                        var body = new { model = actualModelName, messages = new[] { new { role = "system", content = "Ping" }, new { role = "user", content = "Ping" } }, max_tokens = 1, temperature = 0.0 };
                        var res = await testHttpClient.PostAsync(actualApiBaseUrl, new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json"), cts.Token);
                        if (res.IsSuccessStatusCode) return new Dictionary<string, object> { { "success", true }, { "message", "Success: LLM service is available" } };
                        else return new Dictionary<string, object> { { "success", false }, { "message", $"Failed: {res.StatusCode}" } };
                    }
                }
            }
            catch (Exception ex) { return new Dictionary<string, object> { { "success", false }, { "message", $"Failed: {ex.Message}" } }; }
        }

        private static async Task TestLlmConnectionAsync(WebSocket webSocket, dynamic testConfig)
        {
            try
            {
                string modelName = testConfig?.llmModelName?.ToString();
                string apiBaseUrl = testConfig?.llmApiBaseUrl?.ToString();
                string apiKey = testConfig?.llmApiKey?.ToString();
                var result = await CheckLlmApiStatusAsync(modelName, apiBaseUrl, apiKey);
                string message = (string)result["message"];
                await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new WebSocketMessage { Type = "llm_test_result", Data = message }))), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception ex) { LogError("CONTROL_PANEL", "Error testing LLM connection", ex); }
        }

        private static async Task StartControlPanelServerAsync()
        {
            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"http://*:{CONTROL_PANEL_PORT}/");
                _httpListener.Start();
                LogInfo("SYSTEM", $"Control Panel Access Key: {_controlPanelKey}");
                LogInfo("SYSTEM", $"Control Panel URL: http://localhost:{CONTROL_PANEL_PORT}?key={_controlPanelKey}");
                DialogResult result = MessageBox.Show("Do you want to open the control panel?", "Control Panel", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result == DialogResult.Yes) Process.Start(new ProcessStartInfo($"http://localhost:{CONTROL_PANEL_PORT}?key={_controlPanelKey}") { UseShellExecute = true });
                while (!_globalCts.IsCancellationRequested)
                {
                    var context = await _httpListener.GetContextAsync();
                    _ = Task.Run(() => HandleHttpRequestAsync(context));
                }
            }
            catch (Exception ex)
            {
                LogError("CONTROL_PANEL", $"Error starting control panel server on port {CONTROL_PANEL_PORT}", ex);
            }
        }

        private static async Task HandleHttpRequestAsync(HttpListenerContext context)
        {
            try
            {
                if (context.Request.HttpMethod == "GET" && context.Request.Url.PathAndQuery == "/health") ServeHealthCheck(context);
                else if (context.Request.HttpMethod == "GET" &&
                (context.Request.Url.PathAndQuery.StartsWith("/css/") ||
                 context.Request.Url.PathAndQuery.StartsWith("/js/") ||
                 context.Request.Url.PathAndQuery.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase)))
                    ServeStaticFile(context);
                else if (context.Request.HttpMethod == "GET" && context.Request.Url.PathAndQuery == "/unauthorized.html") ServeUnauthorizedHtml(context);
                else
                {
                    if (context.Request.HttpMethod == "GET" && context.Request.Url.PathAndQuery.StartsWith(CONTROL_PANEL_PREFIX))
                    {
                        if (!ValidateControlPanelAccess(context))
                        {
                            await HandleUnauthorizedWebSocketRequestAsync(context);
                            return;
                        }
                        await HandleWebSocketRequestAsync(context);
                    }
                    else
                    {
                        if (!ValidateControlPanelAccess(context))
                        {
                            RedirectToUnauthorized(context);
                            return;
                        }
                        if (context.Request.HttpMethod == "GET" && (context.Request.Url.PathAndQuery == "/" || context.Request.Url.PathAndQuery.StartsWith("/?"))) ServeControlPanelHtml(context);
                        else if (context.Request.HttpMethod == "GET" && context.Request.Url.PathAndQuery == "/api/config") ServeConfig(context);
                        else if (context.Request.HttpMethod == "POST" && context.Request.Url.PathAndQuery == "/api/config") await UpdateConfigAsync(context);
                        else if (context.Request.HttpMethod == "GET" && context.Request.Url.PathAndQuery == "/api/logs") ServeLogs(context);
                        else if (context.Request.HttpMethod == "DELETE" && context.Request.Url.PathAndQuery == "/api/logs") ClearLogs(context);
                        else { context.Response.StatusCode = 404; context.Response.Close(); }
                    }
                }
            }
            catch { context.Response.Close(); }
        }

        private static async Task HandleWebSocketRequestAsync(HttpListenerContext context)
        {
            try
            {
                var wsContext = await context.AcceptWebSocketAsync(null);
                var webSocket = wsContext.WebSocket;
                lock (_controlPanelLock) _controlPanelClients.Add(webSocket);
                BroadcastMessageToClients(new WebSocketMessage { Type = "client_count_updated", Data = _controlPanelClients.Count });
                await SendInitialDataAsync(webSocket);
                await HandleWebSocketMessagesAsync(webSocket);
            }
            catch { context.Response.Close(); }
        }

        private static async Task HandleWebSocketMessagesAsync(WebSocket webSocket)
        {
            try
            {
                var buffer = new byte[1024 * 8];
                var messageBuilder = new StringBuilder();
                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _globalCts.Token);
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                        if (result.EndOfMessage) { string json = messageBuilder.ToString(); messageBuilder.Clear(); await ProcessWebSocketMessageAsync(webSocket, json); }
                    }
                    else if (result.MessageType == WebSocketMessageType.Close) break;
                }
            }
            catch { }
            finally
            {
                lock (_controlPanelLock) _controlPanelClients.Remove(webSocket);
                BroadcastMessageToClients(new WebSocketMessage { Type = "client_count_updated", Data = _controlPanelClients.Count });
            }
        }

        private static async Task ProcessWebSocketMessageAsync(WebSocket webSocket, string json)
        {
            try
            {
                var message = JsonConvert.DeserializeObject<WebSocketMessage>(json);
                switch (message.Type)
                {
                    case "get_logs": await SendLogsAsync(webSocket); break;
                    case "clear_logs": ClearLogs(); BroadcastMessageToClients(new WebSocketMessage { Type = "logs_cleared" }); break;
                    case "clear_context": ClearContext(); BroadcastMessageToClients(new WebSocketMessage { Type = "context_cleared" }); break;
                    case "config_update": UpdateConfig(message.Data); BroadcastMessageToClients(new WebSocketMessage { Type = "config_updated", Data = _controlPanelConfig }); break;
                    case "get_llm_status":
                        bool llmApiAvailable = _lastLlmStatus;
                        if (!_lastLlmStatus || (DateTime.Now - _lastLlmCheckTime).TotalMilliseconds >= LLM_STATUS_CHECK_INTERVAL)
                        {
                            var result = await CheckLlmApiStatusAsync();
                            llmApiAvailable = (bool)result["success"];
                            _lastLlmStatus = llmApiAvailable;
                            _lastLlmCheckTime = DateTime.Now;
                        }
                        await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new WebSocketMessage { Type = "llm_status", Data = llmApiAvailable ? "Online" : "Offline" }))), WebSocketMessageType.Text, true, CancellationToken.None);
                        break;
                    case "test_llm_connection": await TestLlmConnectionAsync(webSocket, message.Data); break;
                    case "get_runtime":
                        double uptime = (DateTime.Now - _startTime).TotalSeconds;
                        await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new WebSocketMessage { Type = "runtime", Data = uptime }))), WebSocketMessageType.Text, true, CancellationToken.None);
                        break;
                    case "test_connection":
                        await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new WebSocketMessage { Type = "connection_test", Data = "Connection test successful" }))), WebSocketMessageType.Text, true, CancellationToken.None);
                        break;
                }
            }
            catch { }
        }

        private static void ClearContext()
        {
            lock (_contextLock)
            {
                _context.Clear();
                _context.Add(new Message { Role = "system", Content = _controlPanelConfig.BaseSystemPrompt });
                LogInfo("CONTROL_PANEL", "Context cleared by user request");
            }
            SaveContextToDisk();
            lock (_eventLock)
            {
                _scheduledEvents.Clear();
            }
            SaveEventsToDisk();
            BroadcastMessageToClients(new WebSocketMessage { Type = "scheduled_events_updated", Data = new List<EventModel>() });
        }

        private static async Task SendInitialDataAsync(WebSocket webSocket)
        {
            try
            {
                List<EventModel> events; lock (_eventLock) events = _scheduledEvents.ToList();
                var initialData = new { logs = GetLogs(), config = _controlPanelConfig, uptime = (DateTime.Now - _startTime).TotalSeconds, scheduledEvents = events, stats = new { totalMessages = _totalMessages, proactiveChats = _proactiveChats, reminders = _reminders } };
                await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new WebSocketMessage { Type = "init", Data = initialData }))), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch { }
        }

        private static async Task SendLogsAsync(WebSocket webSocket) => await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new WebSocketMessage { Type = "logs", Data = GetLogs() }))), WebSocketMessageType.Text, true, CancellationToken.None);
        private static string GenerateSecureKey() { var bytes = new byte[16]; using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(bytes); return BitConverter.ToString(bytes).Replace("-", "").ToLower(); }
        private static bool ValidateControlPanelAccess(HttpListenerContext context) { var key = GetQueryParameter(context.Request.Url.Query, "key"); return !string.IsNullOrEmpty(key) && key == _controlPanelKey; }
        private static string GetQueryParameter(string q, string n) { if (string.IsNullOrEmpty(q)) return null; if (q.StartsWith("?")) q = q.Substring(1); return q.Split('&').Select(p => p.Split('=')).FirstOrDefault(parts => parts.Length == 2 && parts[0].Equals(n, StringComparison.OrdinalIgnoreCase))?[1]; }
        private static void BroadcastMessageToClients(WebSocketMessage message)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));
            lock (_controlPanelLock) { foreach (var client in _controlPanelClients.Where(c => c.State == WebSocketState.Open)) _ = client.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None); }
        }

        private static void ServeControlPanelHtml(HttpListenerContext context)
        {
            try { string path = Path.Combine(Environment.CurrentDirectory, "ControlPanel", "public", "index.html"); if (File.Exists(path)) { byte[] buffer = File.ReadAllBytes(path); context.Response.ContentType = "text/html"; context.Response.OutputStream.Write(buffer, 0, buffer.Length); } } finally { context.Response.Close(); }
        }

        private static void ServeStaticFile(HttpListenerContext context)
        {
            try { string path = Path.Combine(Environment.CurrentDirectory, "ControlPanel", "public", context.Request.Url.PathAndQuery.Substring(1)); if (File.Exists(path)) { byte[] buffer = File.ReadAllBytes(path); context.Response.ContentType = GetContentType(Path.GetExtension(path)); context.Response.OutputStream.Write(buffer, 0, buffer.Length); } } finally { context.Response.Close(); }
        }

        private static string GetContentType(string ext) { ext = ext.ToLower(); return ext == ".css" ? "text/css" : (ext == ".js" ? "application/javascript" : (ext == ".ico" ? "image/x-icon" : "application/octet-stream")); }
        private static void ServeHealthCheck(HttpListenerContext context) { byte[] buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { status = "ok" })); context.Response.OutputStream.Write(buffer, 0, buffer.Length); context.Response.Close(); }
        private static void ServeConfig(HttpListenerContext context) { byte[] buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(_controlPanelConfig)); context.Response.OutputStream.Write(buffer, 0, buffer.Length); context.Response.Close(); }
        private static async Task UpdateConfigAsync(HttpListenerContext context) { using (var r = new StreamReader(context.Request.InputStream)) UpdateConfig(JsonConvert.DeserializeObject<dynamic>(await r.ReadToEndAsync())); context.Response.Close(); }
        private static void ServeLogs(HttpListenerContext context) { byte[] buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(GetLogs())); context.Response.OutputStream.Write(buffer, 0, buffer.Length); context.Response.Close(); }
        private static void ClearLogs(HttpListenerContext context) { ClearLogs(); context.Response.Close(); }

        private static void RedirectToUnauthorized(HttpListenerContext context)
        {
            try
            {
                context.Response.Redirect("/unauthorized.html");
            }
            finally
            {
                context.Response.Close();
            }
        }

        private static void ServeUnauthorizedHtml(HttpListenerContext context)
        {
            try
            {
                string path = Path.Combine(Environment.CurrentDirectory, "ControlPanel", "public", "unauthorized.html");
                if (File.Exists(path))
                {
                    byte[] buffer = File.ReadAllBytes(path);
                    context.Response.ContentType = "text/html";
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                }
            }
            finally
            {
                context.Response.Close();
            }
        }

        private static async Task HandleUnauthorizedWebSocketRequestAsync(HttpListenerContext context)
        {
            try
            {
                var wsContext = await context.AcceptWebSocketAsync(null);
                var webSocket = wsContext.WebSocket;

                string unauthorizedHtml = "";
                string path = Path.Combine(Environment.CurrentDirectory, "ControlPanel", "public", "unauthorized.html");
                if (File.Exists(path))
                {
                    unauthorizedHtml = File.ReadAllText(path);
                }

                var errorResponse = new ErrorResponse
                {
                    Code = ErrorCodes.INVALID_ACCESS_KEY,
                    Message = "Authentication failed, please use the correct access key",
                    Html = unauthorizedHtml
                };

                var errorMessage = new WebSocketMessage
                {
                    Type = "auth_error",
                    Data = errorResponse
                };

                byte[] buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(errorMessage));
                await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);

                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Authentication failed", CancellationToken.None);
            }
            catch { context.Response.Close(); }
        }

        private static void UpdateConfig(dynamic configData)
        {
            lock (_configLock)
            {
                if (configData.llmModelName != null) _controlPanelConfig.LlmModelName = configData.llmModelName.ToString();
                if (configData.llmApiBaseUrl != null) _controlPanelConfig.LlmApiBaseUrl = configData.llmApiBaseUrl.ToString();
                if (configData.llmApiKey != null)
                {
                    _controlPanelConfig.LlmApiKey = configData.llmApiKey.ToString();
                    // Update the HTTP client's authorization header with the new API key
                    _httpClient.DefaultRequestHeaders.Remove(LLM_AUTH_HEADER);
                    _httpClient.DefaultRequestHeaders.Add(LLM_AUTH_HEADER, $"{LLM_AUTH_SCHEME} {_controlPanelConfig.LlmApiKey}");
                }
                if (configData.llmMaxTokens != null) _controlPanelConfig.LlmMaxTokens = (int)configData.llmMaxTokens;
                if (configData.llmTemperature != null) _controlPanelConfig.LlmTemperature = (double)configData.llmTemperature;
                if (configData.llmTopP != null) _controlPanelConfig.LlmTopP = (double)configData.llmTopP;
                if (configData.websocketServerUri != null)
                {
                    string newUri = configData.websocketServerUri.ToString();
                    // Only update and reconnect if the URI has actually changed
                    if (newUri != _controlPanelConfig.WebsocketServerUri)
                    {
                        _controlPanelConfig.WebsocketServerUri = newUri;
                        // Close current WebSocket connection to trigger reconnection with new URI
                        if (_webSocket != null && _webSocket.State == WebSocketState.Open)
                        {
                            try
                            {
                                _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Configuration updated", CancellationToken.None).Wait();
                            }
                            catch { }
                        }
                    }
                }
                if (configData.websocketKeepAliveInterval != null) _controlPanelConfig.WebsocketKeepAliveInterval = (int)configData.websocketKeepAliveInterval;
                if (configData.maxContextRounds != null) _controlPanelConfig.MaxContextRounds = (int)configData.maxContextRounds;
                if (configData.targetUserId != null) _controlPanelConfig.TargetUserId = (long)configData.targetUserId;
                if (configData.activeChatProbability != null) _controlPanelConfig.ActiveChatProbability = (int)configData.activeChatProbability;
                if (configData.minSafeDelay != null) _controlPanelConfig.MinSafeDelay = (int)configData.minSafeDelay;
                if (configData.proactiveChatEnabled != null) _controlPanelConfig.ProactiveChatEnabled = (bool)configData.proactiveChatEnabled;
                if (configData.reminderEnabled != null) _controlPanelConfig.ReminderEnabled = (bool)configData.reminderEnabled;
                if (configData.reinforcementEnabled != null) _controlPanelConfig.ReinforcementEnabled = (bool)configData.reinforcementEnabled;
                if (configData.intentAnalysisEnabled != null) _controlPanelConfig.IntentAnalysisEnabled = (bool)configData.intentAnalysisEnabled;
                if (configData.baseSystemPrompt != null) _controlPanelConfig.BaseSystemPrompt = configData.baseSystemPrompt.ToString();
                if (configData.incompleteInputPrompt != null) _controlPanelConfig.IncompleteInputPrompt = configData.incompleteInputPrompt.ToString();
                if (configData.reinforcementPrompt != null) _controlPanelConfig.ReinforcementPrompt = configData.reinforcementPrompt.ToString();

                // Save configuration to file after update
                SaveConfig();
            }
        }

        private static List<LogEntry> GetLogs() { lock (_logsLock) return _logs.ToList(); }
        private static void ClearLogs() { lock (_logsLock) _logs.Clear(); }

        private static void LogInfo(string s, string m) { AddLog("INFO", s, m); Console.WriteLine($"[{DateTime.Now:T}] [INFO] [{s}] {m}"); LogToFile(GENERAL_LOG_SUBFOLDER, $"[INFO] [{s}] {m}"); }
        private static void LogWarning(string s, string m) { AddLog("WARNING", s, m); Console.WriteLine($"[{DateTime.Now:T}] [WARN] [{s}] {m}"); LogToFile(GENERAL_LOG_SUBFOLDER, $"[WARN] [{s}] {m}"); }
        private static void LogError(string s, string m, Exception ex = null) { string full = ex != null ? $"{m}: {ex.Message}" : m; AddLog("ERROR", s, full); Console.WriteLine($"[{DateTime.Now:T}] [ERROR] [{s}] {full}"); LogToFile(GENERAL_LOG_SUBFOLDER, $"[ERROR] [{s}] {full}"); }

        private static void AddLog(string l, string s, string m)
        {
            lock (_logsLock) { _logs.Add(new LogEntry { Timestamp = DateTime.Now.ToString("HH:mm:ss"), Level = l, Source = s, Message = m }); if (_logs.Count > MAX_LOGS) _logs.RemoveAt(0); }
            BroadcastMessageToClients(new WebSocketMessage { Type = "log", Data = new { timestamp = DateTime.Now.ToString("HH:mm:ss"), level = l, source = s, message = m } });
        }

        private static void LogToFile(string sub, string m)
        {
            try { string dir = Path.Combine(Environment.CurrentDirectory, LOG_ROOT_FOLDER, sub); if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); File.AppendAllText(Path.Combine(dir, $"{DateTime.Now:yyyy-MM-dd}.log"), $"[{DateTime.Now:T}] {m}\n"); } catch { }
        }

        private static void LogAIContext(string hid, List<Message> context)
        {
            try { string dir = Path.Combine(Environment.CurrentDirectory, LOG_ROOT_FOLDER, CONTEXT_LOG_SUBFOLDER); if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); File.AppendAllText(Path.Combine(dir, $"{DateTime.Now:yyyy-MM-dd}_AI_Context.log"), $"\n{new string('-', 30)}\nHID: {hid}\n{JsonConvert.SerializeObject(context, Formatting.Indented)}\n"); } catch { }
        }
    }
}