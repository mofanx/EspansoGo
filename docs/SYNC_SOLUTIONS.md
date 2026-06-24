# Android �?桌面 Espanso 无缝同步方案

## 设计目标

**一次配置，无感同步�?* 用户在桌�?espanso �?Android EspansoGo 之间自由切换，配置自动保持一致，无需手动导入/导出�?

### 目标用户体验

```
首次使用�?
  安装 EspansoGo �?设置向导 �?选择同步方式 �?自动拉取配置 �?完成

日常使用�?
  桌面修改 matches �?保存 �?手机自动更新（无需打开 App�?
  手机修改 matches �?保存 �?桌面自动更新（espanso auto_restart�?

多设备：
  笔记�?+ 台式�?+ 手机，通过同一同步源保持一�?
```

---

## 现状分析

### Espanso 桌面�?

**配置目录结构**�?
```
espanso/
├── config/
�?  ├── default.yml              # 主配�?
�?  └── app-specific.yml         # App 特定配置
├── match/                       # �?同步的核心目�?
�?  ├── base.yml                 # 基础 matches
�?  ├── personal.yml             # 用户自定�?matches
�?  └── *.yml                    # 其他 match 文件
└── packages/                    # �?hub 安装的包
```

**关键特�?*�?
- `auto_restart: true`（默认开启）�?配置文件变化时自动重�?
- 支持 `imports` 跨文件引�?
- IPC 仅用于进程控制（Unix Socket / Named Pipe），�?HTTP API
- 包管理：�?espanso hub（GitHub Releases）下�?zip 并安�?

**配置路径**�?
- Linux：`~/.config/espanso/` �?`~/.espanso/`
- macOS：`~/Library/Application Support/espanso/`
- Windows：`%APPDATA%\espanso\`

### EspansoGo 当前状�?

**内部存储**�?
- `keywords.json` �?所�?matches 序列化为 JSON
- `global.json` �?全局变量
- 导入/导出为单文件 YAML（手动触发）

**关键差距**�?
1. 内部�?JSON 存储，与 espanso 的多文件 YAML 结构不对�?
2. 导入是单文件、手动、一次性的
3. 无后台同步机�?
4. 无文件监控能�?
5. 不支�?`imports` 递归解析

---

## 整体架构设计

### 分层架构

```
┌─────────────────────────────────────────────────────�?
�?                  用户界面�?                         �?
�? 设置向导 · 同步状态指�?· 包商�?· 手动操作            �?
├─────────────────────────────────────────────────────�?
�?                  同步管理�?                         �?
�? SyncManager · 冲突检�?· 变更追踪 · 通知              �?
├──────────────────────┬──────────────────────────────�?
�?  传输�?(可插�?      �?     格式�?                   �?
�? CloudFolder         �? YamlWorkspace               �?
�? GitRepo             �? (多文�?YAML 读写)            �?
�? LocalNetwork        �?                              �?
├──────────────────────┴──────────────────────────────�?
�?                  存储�?                             �?
�? AppData (内部) · SyncFolder (外部) · Cache           �?
└─────────────────────────────────────────────────────�?
```

### 核心设计决策

#### 决策 1：EspansoGo 采用 espanso 原生 YAML 格式作为同步标准

**现状问题**：EspansoGo 内部�?`keywords.json`（单文件 JSON），espanso �?`match/*.yml`（多文件 YAML）。每次导�?导出都需要格式转换，且无法保持文件结构�?

**方案**：引�?`YamlWorkspace` 概念 �?EspansoGo 能直接读�?espanso 的多文件 YAML 结构�?

**实现**�?
- 内部仍用 `Dictionary<string, Match>` 作为运行时模型（AccessibilityService 依赖�?
- 新增 `YamlWorkspace` 服务，负责在 `Dictionary` 和多文件 YAML 之间双向转换
- 同步时以 YAML 文件为单位，保持�?espanso 文件结构一�?
- 每个 YAML 文件对应一�?`MatchGroup`（matches + global_vars + imports），�?espanso �?`MatchGroup` 结构一�?

**关键设计原则**�?

1. **保留未知字段（零数据丢失�?*�?
   - Espanso �?`Params` �?`BTreeMap<String, Value>`（动�?key-value），支持任意变量类型和参�?
   - EspansoGo �?`Params` 类使用强类型字段（`Echo`、`Format`、`Cmd` 等），只能处理已知参�?
   - YamlWorkspace 反序列化 YAML 时，使用 `Dictionary<string, object>` 作为中间表示�?*保留所有字�?*
   - 运行时模型（`Dict<string, Match>`）只解析已知字段用于扩展执行
   - 序列化回 YAML 时，将未知字段原样写回，确保同步不丢失数�?
   - 实现：使�?`YamlDotNet` �?`Deserializer` 配合 `dynamic` / `Dictionary<string, object>` 解析

2. **`global_vars` 不拆分到单独文件**�?
   - Espanso 允许在任�?match YAML 文件中定�?`global_vars`（与 `matches` 并列�?
   - YamlWorkspace 保持原始文件结构，`global_vars` 留在原始文件中，不集中拆分到 `global_vars.yml`
   - 导出时新建的 `global_vars` 写入用户当前编辑的文件或 `EspansoGo.yml`

3. **`imports` 解析：区�?config �?glob �?match 文件层逐文件解�?*�?

   > **关键发现**：espanso 有两套不同的路径解析机制，YamlWorkspace 必须分别实现�?

   - **Config 层（glob 模式匹配�?*�?
     - `STANDARD_INCLUDES`: `["../match/**/[!_]*.yml", "../match/**/[!_]*.yaml"]`
     - �?`calculate_paths`（`path.rs:30-74`）使�?Rust `glob` crate 执行
     - `**` 递归匹配子目录，`[!_]*` 排除 `_` 前缀文件
     - `includes`、`extra_includes` 字段也通过此机制解�?
     - `excludes`、`extra_excludes` 字段同样通过 glob 解析，最�?`match_paths = include_paths - exclude_paths`
     - YamlWorkspace �?`ReadFromFolder` 必须实现�?glob 逻辑来发现所�?match 文件

   - **Match 文件层（逐文件路径解析，�?glob�?*�?
     - �?`resolve_imports`（`group/path.rs:26-86`）执�?
     - 每个 import 路径直接 `canonicalize` + 检�?`is_file`
     - **不支�?glob 模式**，只解析具体文件路径
     - 路径可以是相对路径（相对于当前文件所在目录）或绝对路�?
     - YamlWorkspace 解析单个 YAML 文件�?`imports` 字段时，使用此逐文件逻辑
     - 当前 EspansoGo �?`ProcessImport`（`Index.razor:396-413`）已实现此逻辑

   - **.NET glob 兼容性问�?*�?
     - `Microsoft.Extensions.FileSystemGlobbing` **不支�?* `[!_]*` 字符类否定语�?
     - Rust `glob` crate �?`[!_]` 等价于正则的 `[^_]`
     - **建议方案**：手动遍�?+ 过滤实现 glob�?
       1. 递归遍历目录获取所�?`.yml`/`.yaml` 文件
       2. 过滤掉文件名�?`_` 开头的文件（`StartsWith("_")`�?
       3. 不依�?`FileSystemGlobbing`，避免语法不兼容
     - 或使�?`System.Text.RegularExpressions` �?glob 模式转换为正则表达式

4. **同步范围扩展：包�?`config/` 中的 match 相关字段**�?
   - `config/default.yml` 不整体同步（`backend`、`toggle_key` 等桌面专属配置不相关�?
   - 需要同步的 config 字段（决定哪�?match 文件被加载）�?
     - `use_standard_includes`（是否加�?`STANDARD_INCLUDES`�?
     - `includes` / `extra_includes`（额外的 glob 模式 include 路径�?
     - `excludes` / `extra_excludes`（glob 模式 exclude 路径，`match_paths = includes - excludes`�?
     - `match_paths`（显式指定的 match 文件路径列表�?
   - **app-specific 配置文件**：espanso �?`store.rs` 会加�?`config/` 目录下所�?`.yml` 文件（非 `default.yml`），每个都可能有独立�?`match_paths`/`includes`/`excludes`
   - YamlWorkspace 增加 `ConfigSyncExtractor`，遍�?`config/` 下所�?`.yml` 文件，提取上述字�?
   - 不同步这些字段会导致 match 引用丢失（文件存在但不被加载�?

**好处**�?
- 同步粒度�?整个配置"变为"单个文件"，冲突更�?
- 桌面�?espanso 无需任何改动，`auto_restart` 自动处理文件变化
- 保留 espanso �?`imports` 结构�?`global_vars` 原始位置
- 未知变量类型和参数不丢失，确保双向同步的完整�?

#### 决策 2：传输层可插拔，自动选择最优方�?

用户在设置向导中选择同步方式后，SyncManager 自动处理后续所有传输逻辑�?

| 传输方式 | 触发机制 | 方向 | 实时�?| 适用场景 | 用户门槛 |
|---------|---------|------|--------|---------|---------|
| **CloudFolder (SAF)** | WorkManager 定时 + 即时写入 | 双向 | 分钟�?| 普通用户，已有云存�?| �?|
| **Syncthing** | 文件监控 + 即时推�?| 双向 | 秒级 | 无云依赖，P2P 直连 | �?|
| **WebDAV** | 短轮�?+ 即时推�?| 双向 | 秒级 | 自建 NAS / Nextcloud / 坚果�?| �?|
| **GitRepo** | WorkManager 定时 + 手动 | 双向 | 分钟�?| 开发者，需要版本历�?| �?|
| **LocalNetwork** | 按需（同局域网时） | 双向 | 秒级 | 高频同步，无云依�?| �?|
| **ManualFile** | 用户手动触发 | 单次 | N/A | 偶尔同步，无云服�?| �?|

#### 决策 3：即时推�?+ 智能轮询

**Android 本地修改 �?即时推�?*�?
- 用户�?EspansoGo 中保�?match 时，立即触发 `Push()` 写入同步�?
- 无需等待轮询周期，延�?< 1 �?

**远端修改 �?Android 感知**（按传输方式自动选择）：
- **CloudFolder (SAF)**：WorkManager 周期检查（15 分钟），SAF 无主动通知能力
- **Syncthing**：Syncthing app 自身监控文件变化，EspansoGo 通过 SAF 读取同步文件夹，WorkManager 15 分钟检�?+ 前台时手动刷�?
- **WebDAV**：短轮询 `PROPFIND`（默�?30s，WiFi 下可配置更短），一次请求仅�?KB
- **GitRepo**：WorkManager 周期 `pull`�?5 分钟�?
- **LocalNetwork**：WebSocket 长连接，服务端推送（秒级�?

**通知链路**�?
- 文件变化时通过 `WeakReferenceMessenger` 通知 AccessibilityService 更新 `dict`
- 同步状态显示在 UI 顶部（上次同步时间、状态图标、同步方式）

**桌面�?*�?
- 无需额外开�?�?espanso �?`auto_restart` 已内置文件监�?
- 用户只需�?espanso �?`match/` 目录挂载�?WebDAV 或放入同步文件夹

---

## 传输方案详细设计

### 方案 A：WebDAV（进阶推荐）�?自建�?/ NAS 最优解

**原理**：EspansoGo 内置 WebDAV 客户端，直接�?WebDAV 服务器通信。桌面端通过 WebDAV 客户端（�?rclone、davfs2）将 espanso match 目录挂载�?WebDAV�?

**桌面端设�?*（一次性）�?
```bash
# 方法 1：rclone 挂载（推荐，跨平台）
rclone mount webdav:espanso-match ~/.config/espanso/match --vfs-cache-mode writes

# 方法 2：davfs2（Linux 原生�?
mount -t davfs https://nas.local/dav/espanso-match ~/.config/espanso/match

# 方法 3：Nextcloud / 坚果云桌面客户端同步 espanso match 目录
```

**Android 端设�?*（一次性）�?
1. 设置向导 �?选择"WebDAV 同步"
2. 输入 WebDAV 服务器地址、用户名、密�?
3. 指定远程路径（如 `/espanso-match/`�?
4. EspansoGo 测试连接 �?保存配置
5. 后台短轮询自动检查变�?

**同步流程**�?
```
桌面修改 �?保存�?match/*.yml �?WebDAV 客户端自动上�?
                                    �?
Android 短轮�?PROPFIND�?0s）→ 检测到 ETag/时间戳变�?
                                    �?
                        GET 变化的文�?�?解析 YAML �?更新 dict
                                    �?
                            通知 AccessibilityService

Android 修改 �?保存时即�?PUT �?WebDAV 服务器（< 1s�?
                                    �?
桌面 WebDAV 客户端同步到本地 �?espanso auto_restart 检测变�?�?自动重载
```

**技术要�?*�?
- WebDAV 协议操作（基�?`HttpClient`）：
  - `PROPFIND`（Depth: 1）�?列目录，返回文件列表 + ETag + Last-Modified
  - `GET` �?下载文件内容
  - `PUT` �?上传文件（即时推送）
  - `MKCOL` �?创建目录
  - `DELETE` �?删除文件
- 变化检测：优先使用 `ETag`，回退�?`Last-Modified` 时间�?
- 短轮询策略：
  - WiFi 下默�?30s，可配置 10s~5min
  - 移动网络下降级为 5 分钟
  - App 在前台时轮询，后台时切换�?WorkManager 15 分钟
- 认证：HTTP Basic Auth，凭据存储在 `AndroidKeyStore`
- `HttpClient` 自定�?method 支持�?*高风险项，需早期原型验证**）：
  - Android �?`HttpClient` 默认使用 OkHttp 引擎，发�?`PROPFIND`/`MKCOL` 等非标准 method 需自定�?`HttpMethod`
  - **建议实现**：子类化 `HttpMessageHandler` + 使用 `HttpRequestMessage` 自定�?`Method` 属�?
  - **早期原型验证**：在第一期开发前，先写一个最小化 PoC 验证 `AndroidClientHandler` 能否发�?`PROPFIND` 请求并解�?XML 响应
  - 如果 `AndroidClientHandler` 不支持，fallback 方案：使用平台特定代码调�?`OkHttp`（通过 JNI/Binding）或使用 `SocketsHttpHandler` + 手动构建 HTTP 请求
- WebDAV 服务器兼容性矩阵（需测试）：

  | 服务�?| PROPFIND Depth:1 | ETag 支持 | 坚果云特�?| 测试优先�?|
  |--------|-----------------|----------|-----------|-----------|
  | Nextcloud | 完整 | 完整 | N/A | �?|
  | 坚果�?| 完整 | 完整 | 不支�?`Depth: infinity`，需�?`Depth: 1` | �?|
  | Synology NAS | 完整 | 完整 | N/A | �?|
  | rclone serve webdav | 完整 | 完整 | N/A | �?|
  | Apache mod_dav | 完整 | 完整 | N/A | �?|

**优点**�?
- 即时推送（Android 修改 �?PUT < 1s�?
- 快速感知（桌面修改 �?Android 30s 内检测到�?
- 支持自建 NAS / Nextcloud / 坚果云，隐私可控
- 不需要额�?Android App（WebDAV 客户端内置）
- 不增�?APK 体积（`HttpClient` �?.NET 内置�?
- 桌面端选择丰富（rclone / davfs2 / Nextcloud 客户端等�?

**缺点**�?
- 需要用户有 WebDAV 服务器（NAS / Nextcloud / 坚果云等�?
- 短轮询有少量流量消耗（每次 PROPFIND ~1-2KB�?
- 需处理 WebDAV 服务器兼容性差异（部分实现不完全符�?RFC 4918�?

**改动�?*：中�?�?WebDAV 客户�?+ SyncManager + YamlWorkspace + 设置向导 UI

---

### 方案 B：CloudFolder（SAF 云文件夹同步）�?默认推荐，商业云存储

**原理**：通过 Android Storage Access Framework（SAF）选择云存储客户端（Google Drive、Dropbox、OneDrive 等）同步的文件夹，EspansoGo 读写该文件夹�?

**桌面端设�?*（一次性）�?
```bash
# �?espanso match 目录 symlink 到云同步文件�?
ln -s ~/Google\ Drive/espanso-match/ ~/.config/espanso/match
# 或在云客户端中添�?~/.config/espanso/match/ 为同步文件夹
```

**Android 端设�?*（一次性）�?
1. 设置向导 �?选择"云文件夹同步"
2. 使用 SAF 选择云存储中�?espanso-match 文件�?
3. EspansoGo 保存文件�?URI（持久化权限�?
4. 后台 WorkManager 定时检查文件夹变化

**同步流程**�?
```
桌面修改 �?保存�?match/*.yml �?云客户端自动上传
                                    �?
Android WorkManager 检测到文件变化（≤15分钟）→ 解析 YAML �?更新 dict
                                    �?
                            通知 AccessibilityService

Android 修改 �?序列化为 YAML �?写入云文件夹 �?云客户端自动上传
                                    �?
桌面云客户端同步到本�?�?espanso auto_restart 检测变�?�?自动重载
```

**技术要�?*�?
- SAF 持久�?URI：通过 `ContentResolver.TakePersistableUriPermission` 保存访问权限
- 文件变化检测（双重保障）：
  - 优先使用 `last_modified` 时间戳对�?
  - Fallback：对文件内容计算 MD5 hash，对�?hash 变化（应对部�?SAF provider 修改时间不准确的情况，如 Google Drive�?
  - 本地缓存文件列表 + hash 映射，避免每次全量列举远程文�?
- SAF 性能优化�?
  - 云存�?SAF provider 列举远程文件速度较慢（每次可能数秒到数十秒）
  - 首次同步做全量拉�?+ 本地缓存，后续只检�?hash 变化
  - 前台时提供手�?立即同步"按钮，不依赖 WorkManager 周期
- **SAF 延迟缓解策略**�?5 分钟 WorkManager 延迟对用户体验影响较大）�?
  - **前台 ContentObserver**：App 在前台时，注�?`ContentObserver` 监听 SAF URI 变化（部�?SAF provider 支持 `notifyChange`，如 Google Drive�?
  - **前台短轮�?fallback**：对不支�?`notifyChange` �?SAF provider，前台时�?60s 主动检查一次（轻量 hash 对比�?
  - **显著 UI 提示**：主界面顶部显示同步状态指示器（上次同步时�?+ "立即同步"按钮），避免用户误认为同步失�?
  - **Foreground Service**：App 在前台时启动短暂 Foreground Service 执行同步检查（Android 14+ 限制后台启动 Service，前台时不受影响�?
- WorkManager 约束：仅�?WiFi 连接时同步（可选），电池优�?
- 多文件处理：遍历文件夹中所�?`.yml`/`.yaml` 文件（含子目录，排除 `_` 前缀），逐个解析合并
- 即时推送：Android 修改保存时立即写�?SAF 文件夹（云客户端自动上传�?
- 桌面�?Windows 特别说明：Windows 不支�?symlink，需在云客户端中直接添加 `match/` 目录为同步文件夹

**优点**�?
- 用户只需选择一次文件夹，之后完全自�?
- 桌面端零改动（利�?espanso 现有 `auto_restart`�?
- 支持所有主流云服务（Google Drive、Dropbox、OneDrive、Nextcloud 等）
- 不增�?APK 体积（SAF �?Android 原生�?

**缺点**�?
- 远端变化感知有延迟（WorkManager 最�?15 分钟），前台 ContentObserver/短轮询可缓解但仍非实�?
- 依赖云客户端的同步速度
- 需要桌面端安装对应云客户端
- 部分 SAF provider 不支�?`notifyChange`，前台只能靠短轮�?

**改动�?*：小 �?SAF API 已内置，主要工作�?UI 和文件选择逻辑

---

### 方案 B2：Syncthing（P2P 文件同步）�?无云依赖，实时同�?

**原理**：利�?Syncthing（开�?P2P 文件同步工具）在桌面�?Android 之间直接同步 espanso match 目录。EspansoGo 通过 SAF 读取 Syncthing app 同步的本地文件夹，Syncthing 自身处理 P2P 传输和文件监控�?

**桌面端设�?*（一次性）�?
```bash
# 1. 安装 Syncthing（https://syncthing.net/�?
#    macOS: brew install syncthing
#    Linux: sudo apt install syncthing
#    Windows: choco install syncthing

# 2. �?Syncthing Web UI 中创建共享文件夹，指�?espanso match 目录
#    文件夹路径：~/.config/espanso/match（或 %APPDATA%\espanso\match�?
#    文件�?ID：espanso-match

# 3. 添加 Android 设备为共享节点（扫描设备 ID�?
```

**Android 端设�?*（一次性）�?
1. 安装 Syncthing app（推�?Syncthing-Fork，支持前台服务）
2. �?Syncthing app 中创建共享文件夹，与桌面端配�?
3. 设置向导 �?选择"Syncthing 同步"
4. 使用 SAF 选择 Syncthing 同步的文件夹
5. EspansoGo 保存文件�?URI（持久化权限�?
6. WorkManager 定时检�?+ 前台手动刷新

**同步流程**�?
```
桌面修改 �?保存�?match/*.yml �?Syncthing 检测变化（秒级）→ P2P 传输�?Android
                                                                    �?
Android Syncthing 写入本地文件�?�?EspansoGo WorkManager/手动刷新检测到变化
                                                                    �?
                                                          解析 YAML �?更新 dict �?通知 AccessibilityService

Android 修改 �?序列�?YAML �?写入 Syncthing 文件�?�?Syncthing P2P 传输到桌�?
                                                                    �?
                                          espanso auto_restart 检测变�?�?自动重载
```

**技术要�?*�?
- EspansoGo 端实现与 CloudFolder (SAF) 完全相同 �?都是通过 SAF 读写本地文件�?
- 区别在于：Syncthing app 负责文件传输（而非云存储客户端），P2P 直连无需云服务器
- 文件变化检测：�?CloudFolder 相同的双重保障（时间�?+ MD5 hash fallback�?
- Syncthing 自身有文件监控（inotify/fsevents），桌面端文件变化秒级同步到 Android 本地
- WorkManager 15 分钟检�?+ 前台手动"立即同步"按钮
- Syncthing 内置冲突处理：冲突文件保存为 `*.sync-conflict-*.yml`，EspansoGo 可检测并提示用户

**优点**�?
- 无云依赖，P2P 直连，隐私友�?
- 实时性接�?WebDAV（Syncthing 秒级感知 + 传输�?
- 不增�?APK 体积（Syncthing 是独�?app�?
- 桌面端跨平台支持（Windows/macOS/Linux�?
- Syncthing 自带版本冲突处理
- 支持多设备同�?

**缺点**�?
- 需要用户安�?Syncthing app（额外一步）
- 两端需同时在线才能同步（P2P 特性，非云端中转）
- SAF 读取仍有 15 分钟 WorkManager 延迟（但前台可手动刷新）
- 需要用户配�?Syncthing 共享文件夹和设备配对

**改动�?*：小 �?复用 CloudFolder (SAF) 的全部代码，仅增�?Syncthing 文件夹检测和冲突文件处理逻辑

**�?CloudFolder 的关�?*�?
- 技术实现完全复�?SAF 读写逻辑
- 用户选择"Syncthing 同步"时，UI 引导用户先安�?Syncthing app 并配�?
- 代码层面仅增�?`SyncMethod = Syncthing` 枚举�?+ 冲突文件检测（`*.sync-conflict-*` 模式匹配�?

---

### 方案 C：GitRepo（Git 仓库同步）�?开发者首�?

**原理**：将 espanso match 配置存放�?Git 仓库中，两端通过 Git 操作同步�?

**桌面端设�?*�?
```bash
cd ~/.config/espanso/match
git init
git remote add origin git@github.com:user/espanso-config.git
# 可选：设置自动 commit + push �?cron job �?git hooks
```

**Android 端设�?*�?
1. 设置向导 �?选择"Git 同步"
2. 输入仓库 URL + PAT（Personal Access Token�?
3. EspansoGo 执行 `clone` 到本地缓存目�?
4. WorkManager 定时 `pull` / 修改�?`commit + push`

**同步流程**�?
```
桌面修改 �?git add + commit + push（手动或自动�?
                    �?
Android WorkManager �?git pull �?解析 YAML �?更新 dict

Android 修改 �?序列�?YAML �?git add + commit + push
                    �?
桌面 �?git pull（手动或 cron）→ espanso auto_restart
```

**技术要�?*�?
- Git 库选择（按优先级）�?
  1. **Termux git（推荐）**：调�?Termux �?`git` 命令，无 APK 增量，功能完整，但需用户安装 Termux
  2. **libgit2sharp**：~5MB APK 增量，需验证 Android �?ABI（arm64-v8a, armeabi-v7a, x86_64）的 native library 兼容�?
  3. **NGit（纯 .NET�?*：无 native 依赖，但功能不完整（不支�?LFS 等），基础 clone/pull/push 可用
- 认证：PAT token 存储�?`AndroidKeyStore` �?
- 冲突处理：`rebase` 策略，失败时保留两份让用户手动选择
- 自动化：桌面端可提供 `espanso-sync` 脚本（git auto-commit + push�?
- SSH 认证：如使用 SSH URL，需�?Termux 中配�?SSH key；libgit2sharp 不支�?SSH（仅 HTTPS + PAT�?

**优点**�?
- 完整版本历史，可回滚
- 多设备天然支�?
- 不依赖额外云客户端（Git 本身就是同步工具�?
- �?espanso hub �?GitHub 生态契�?

**缺点**�?
- APK 增加 ~5MB（libgit2sharp）或依赖 Termux 安装
- 需�?Git 知识
- 认证配置对普通用户不友好
- libgit2sharp �?Android 上的 native library 兼容性需充分测试

**改动�?*：较�?�?libgit2sharp 集成 + 认证管理 + 冲突处理 UI

---

### 方案 D：LocalNetwork（局域网同步）�?零云依赖

**原理**：桌面端运行一个轻�?HTTP 服务，EspansoGo 在同一局域网内直接通信�?

**桌面�?*：开�?`espanso-sync` companion 工具（小�?Rust/Go 二进制）�?
- 监听 `0.0.0.0:8765`
- `GET /api/files` �?返回 match 目录文件列表 + 内容
- `POST /api/files` �?接收 EspansoGo 推送的文件
- mDNS 广播 `_espanso-sync._tcp` 供自动发�?
- 二维码显示连接信息（IP + 端口 + 配对码）

**Android �?*�?
- 设置向导 �?选择"局域网同步" �?扫描桌面二维�?
- WorkManager �?WiFi 连接时自动检测桌面服�?
- 检测到变化时拉�?推�?

**优点**�?
- 无云依赖，隐私友�?
- 延迟低（局域网直连�?
- 可扩展为远程触发扩展

**缺点**�?
- 需要开发桌面端 companion 工具
- 仅限同一局域网
- 需处理安全性（配对码认证）

**改动�?*：大 �?桌面端工�?+ Android �?HTTP 客户�?+ mDNS + 配对 UI

---

### 方案 E：Espanso Hub 客户�?�?社区包获�?

**原理**：复�?espanso hub 的包索引和下载机制，EspansoGo 直接�?hub 安装社区包�?

**实现**�?
- 包索引：`GET https://github.com/espanso/hub/releases/latest/download/package_index.json`
- 下载�?zip �?SHA256 校验 �?解压 �?解析 `package.yml` �?导入 matches
- 本地缓存包索引（1 小时有效期，�?espanso 一致）

**与同步方案的关系**�?
- Hub 客户端不是同步方案，而是配置获取的补�?
- 安装的包可以纳入 CloudFolder/GitRepo 的同步范�?
- 桌面�?`espanso package install` �?Android 端安装的包通过同步保持一�?

**优点**�?
- 直接复用 espanso 生态，数百个现成包
- 用户体验好：浏览 �?一键安�?
- 与桌面端使用相同包源

**缺点**�?
- GitHub Releases 国内访问可能受限
- 只能下载，不能上传（非双向同步）

**改动�?*：中�?�?HTTP 客户�?+ zip 解压 + SHA256 + 包浏�?UI

---

## 推荐方案组合

### 主方案：CloudFolder + Syncthing + WebDAV + Hub 客户�?

| 需�?| 方案 | 理由 |
|------|------|------|
| 个人 matches 双向同步（商业云�?| CloudFolder | 利用现有云存储，零额外服务端，用户门槛最�?|
| 个人 matches 双向同步（P2P�?| Syncthing | 无云依赖，实时同步，隐私友好 |
| 个人 matches 双向同步（自建云�?| WebDAV | 即时推�?+ 秒级感知，隐私可�?|
| 社区包获�?| Hub 客户�?| 复用 espanso 生态，一键安�?|
| 开发者高级同�?| GitRepo（可选） | 版本历史，多设备 |

**方案选择逻辑**（设置向导中的智能推荐）�?
```
用户已有云存储客户端（Google Drive / Dropbox / OneDrive）？
  └─ �?�?推荐 CloudFolder（SAF）�?零额外安装，选择文件夹即�?
  └─ �?�?用户愿意安装 Syncthing app�?
          └─ �?�?推荐 Syncthing �?P2P 实时同步，无云依�?
          └─ �?�?用户�?NAS / Nextcloud / 坚果云？
                  └─ �?�?推荐 WebDAV �?秒级感知，隐私可�?
                  └─ �?�?手动导入 �?GitRepo（开发者）
```

### 完整用户旅程

```
┌──────────────────────────────────────────────────────────────�?
�? 首次设置                                                     �?
�?                                                             �?
�? 1. 安装 EspansoGo                                          �?
�? 2. 设置向导启动                                              �?
�?    ├─ "你已经在使用桌面 espanso 吗？"                        �?
�?    �?  ├─ �?�?选择同步方式                                  �?
�?    �?  �?  ├─ �?云文件夹同步（推荐）�?SAF 选择文件�?�?拉取 �?
�?    �?  �?  ├─ 🔄 Syncthing 同步 �?引导安装 app + SAF 选择   �?
�?    �?  �?  ├─ 🌐 WebDAV 同步（进阶）�?输入服务器信�?�?拉取 �?
�?    �?  �?  ├─ 📦 Git 仓库 �?输入 URL + PAT �?clone �?解析  �?
�?    �?  �?  └─ 📄 手动导入 �?文件选择�?�?导入               �?
�?    �?  └─ �?�?空白配置开�?/ 浏览包商�?                   �?
�?    └─ 同步配置完成，显示导入摘�?                            �?
�?                                                             �?
�? 3. 开始使�?                                                 �?
└──────────────────────────────────────────────────────────────�?

┌──────────────────────────────────────────────────────────────�?
�? 日常使用 �?桌面修改                                          �?
�?                                                             �?
�? 1. 用户在桌面编�?personal.yml                               �?
�? 2. espanso auto_restart 自动重载                            �?
�? 3. 同步工具自动上传                                          �?
�? 4. Android 感知变化�?                                       �?
�?    ├─ CloudFolder：WorkManager �?5分钟检测到                �?
�?    ├─ Syncthing：Syncthing 秒级传输 + WorkManager 检�?     �?
�?    └─ WebDAV：短轮询 30s 内检测到 �?GET 拉取                �?
�? 5. 解析 YAML �?更新 dict �?通知 AccessibilityService        �?
�? 6. 下次用户打字时，�?matches 已生�?                        �?
└──────────────────────────────────────────────────────────────�?

┌──────────────────────────────────────────────────────────────�?
�? 日常使用 �?Android 修改                                      �?
�?                                                             �?
�? 1. 用户�?EspansoGo 中添�?编辑 match                       �?
�? 2. 保存时即时推送：序列�?YAML �?写入同步源（< 1s�?         �?
�? 3. 桌面端同步到本地                                          �?
�?    ├─ CloudFolder：云客户端自动同�?                         �?
�?    ├─ Syncthing：P2P 秒级传输                                �?
�?    └─ WebDAV：rclone/davfs2 实时同步                        �?
�? 4. espanso auto_restart 检测变�?�?自动重载                  �?
└──────────────────────────────────────────────────────────────�?

┌──────────────────────────────────────────────────────────────�?
�? 社区包安�?                                                 �?
�?                                                             �?
�? 1. 用户打开"包商�? Tab                                      �?
�? 2. 浏览/搜索 espanso hub 包列�?                             �?
�? 3. 点击安装 �?下载 zip �?校验 �?解压 �?导入 matches         �?
�? 4. 安装的包自动纳入同步范围                                   �?
�? 5. 桌面端通过同步获得相同的包                                �?
└──────────────────────────────────────────────────────────────�?
```

---

## 技术实现细�?

### 1. YamlWorkspace 服务

负责�?EspansoGo 内部 `Dictionary<string, Match>` �?espanso 多文�?YAML 之间转换�?

```
YamlWorkspace
├── ReadFromFolder(uri)     �?遍历文件夹中所�?.yml/.yaml �?合并�?Dictionary
├── WriteToFolder(uri, dict) �?拆分为多�?.yml 文件写入
├── ReadFile(uri)           �?解析单个 YAML 文件 �?MatchGroup（保留未知字段）
├── WriteFile(uri, group)   �?序列化单�?YAML 文件（未知字段原样写回）
├── GetFileList(uri)        �?返回文件列表 + 时间�?+ MD5 hash（用于变化检测）
├── ResolveImports(baseDir, imports) �?逐文件路径解析（�?glob），递归加载 import �?YAML 文件
└── ExtractConfigFields(configUri) �?�?config/default.yml 提取 match 相关字段
```

**文件拆分策略**�?
- 导入时：保留原始文件结构（每�?.yml/.yaml 文件独立解析�?
- 导出时：按来源文件分组写入；新建�?matches 写入 `EspansoGo.yml`
- `global_vars` 保留在原始文件中，不拆分到单独文件（�?espanso 惯例一致）
- 子目录结构保留（espanso �?`STANDARD_INCLUDES` 使用 `**` 递归匹配�?

**YAML 读写中间表示**�?
- 使用 `Dictionary<string, object>` 作为 YAML 反序列化的中间类�?
- 已知字段映射�?`Match`/`Var`/`Params` 强类型对象（运行时使用）
- 未知字段保留在中间字典中（序列化时原样写回）
- 实现：`YamlDotNet` �?`Deserializer` + `Dictionary<string, object>`，已在项目依赖中（`YamlDotNet 15.1.4`�?

### 2. SyncManager 服务

```
SyncManager
├── 配置
�?  ├── SyncMethod (Cloud / Syncthing / WebDAV / Git / Local / Manual)
�?  ├── SyncUri (WebDAV URL / SAF URI / Git URL / HTTP URL)
�?  ├── Credentials (用户名密�?/ PAT / 配对�?�?存储�?AndroidKeyStore)
�?  ├── SyncInterval (前台轮询: 30s~5min / 后台: 15min / Manual only)
�?  ├── LastSyncTime
�?  ├── FileETags (文件 �?ETag/时间�?hash 映射，用于变化检�?
�?  └── SyncState (同步状态，持久化到同步文件�?.EspansoGo-sync.json)
├── Push()     �?即时推送：YamlWorkspace.WriteToFolder �?写入同步�?
├── Pull()     �?从同步源拉取 �?YamlWorkspace.ReadFromFolder �?更新 dict
├── CheckChanges() �?对比 ETag/时间�?hash，检测是否有变化
├── ResolveConflicts() �?冲突处理策略（见下方�?
└── CreateSnapshot() �?同步前保存当前状态快照（第三期三方合并使用）
```

**冲突处理策略**（分阶段实现，第一期简化，后续逐步增强）：

> **实施原则**：先验证端到端链路，再逐步增加合并复杂度。三方合并的 YAML 结构感知（trigger �?+ 字段级）实现复杂度高，推迟到第三期�?

**第一期：简单策�?*

1. **ETag/hash 快速检�?*（WebDAV / CloudFolder / Syncthing）：
   - 拉取前先检查远程文�?ETag/hash 是否与上次同步时一�?
   - 如果远程未变 + 本地有修�?�?直接 Push，无冲突
   - 如果远程已变 + 本地未变 �?直接 Pull，无冲突
   - 如果远程已变 + 本地已变 �?进入冲突处理

2. **Last-Write-Wins（LWW，默认）**�?
   - 按文件修改时间戳，较新的版本覆盖较旧�?
   - 适用于绝大多数场景（用户通常不会同时在两端编辑同一文件�?
   - 实现简单，无依�?

3. **冲突保留两份**（LWW 的安�?fallback，可配置）：
   - 保留 `local_*.yml` �?`remote_*.yml` 两份文件
   - 通知用户手动选择或编�?
   - UI 显示冲突详情（文件名、trigger 列表对比�?
   - 用户可在设置中选择"LWW"�?保留两份"作为默认策略

4. **Git 模式**：使�?Git �?merge/rebase 机制，冲突时 `*.orig` 备份

5. **Syncthing 模式**：检�?`*.sync-conflict-*.yml` 文件，提示用户在 UI 中选择保留哪份

**第三期：三方合并（YAML 结构感知，增强）**

6. **三方合并**（所有传输方式）�?
   - 前提：每次同步前 `CreateSnapshot()` 保存当前文件状态快�?
   - 对比三方：Base（上次同步快照）+ Local（本地当前）+ Remote（远程当前）
   - **Match 级别合并**（以 `trigger`/`triggers` 为唯一标识）：
     - Base 中有、Local 删除、Remote 未改 �?删除
     - Base 中有、Remote 删除、Local 未改 �?删除
     - Base 中有、Local �?Remote 都修改了同一 trigger �?**整体替换**（保�?Remote 版本 + UI 警告�?
       > 注：不做字段级合并（vars 列表无序、params 嵌套字典，字段级合并复杂度过高，ROI 低）
     - Base 中没有、Local 新增、Remote 新增不同 trigger �?都保�?
     - Base 中没有、Local �?Remote 新增相同 trigger �?保留 Remote 版本 + UI 警告
   - **global_vars 级别合并**：以 `name` 为唯一标识，整体替换逻辑同上
   - 合并结果写入新文件，更新 BaseSnapshot

### 3. 同步状态持久化

**问题**：SyncManager �?`FileETags`/`SyncState` 如果仅存�?App 内部存储（`AppDataDirectory`），App 重装或清除数据后会丢失，导致首次同步变成全量覆盖而非增量�?

**方案**：同步状态同时写入同步文件夹的隐藏文�?`.EspansoGo-sync.json`�?

```json
{
  "version": 1,
  "lastSyncTime": "2024-01-15T10:30:00Z",
  "files": {
    "match/personal.yml": { "etag": "abc123", "hash": "md5:...", "size": 1024 },
    "match/base.yml": { "etag": "def456", "hash": "md5:...", "size": 512 }
  }
}
```

**设计要点**�?
- `.EspansoGo-sync.json` 写入同步文件夹根目录（前缀 `.` 避免�?espanso 加载�?
- 每次同步完成后更新此文件
- App 启动时优先读取同步文件夹中的 `.EspansoGo-sync.json`，与本地缓存合并
- App 重装后从同步文件夹恢复状态，实现增量同步
- 此文件本身也通过同步传输到其他设备，但各设备独立维护（写入时加设�?ID 区分，或各设备只读自己的状态）
- **简化方�?*：如果多设备状态冲突复杂，可改为各设备在内部存储维护状�?+ 同步文件夹仅存一�?共享"状态（最后同步设备的快照），新设�?重装设备用此快照做首次增量对�?

### 4. 同步调度策略

```
前台短轮询（WebDAV 专用�?
├── 触发：App 在前台时启动，后台时停止
├── 间隔：WiFi 30s（可配置 10s~5min），移动网络 5min
├── 逻辑�?
�?  1. PROPFIND 远程目录 �?对比 ETag/时间�?
�?  2. 如有变化 �?GET 变化的文�?�?解析 YAML �?更新 dict
�?  3. 更新 FileETags 缓存
└── 耗电：单�?PROPFIND ~1-2KB 流量�?0s 间隔可忽�?

后台 WorkManager（所有传输方式）
├── 约束：WiFi 连接（可选）、电池不�?
├── 周期�?5 分钟（最短允许值）
├── 逻辑�?
�?  1. CheckChanges() �?检测远程文件变�?
�?  2. 如有变化 �?Pull() �?更新 dict
�?  3. 检测本�?dict 是否有未推送的修改
�?  4. 如有 �?Push()
�?  5. 更新 LastSyncTime �?发送通知
└── 失败重试：指数退�?

即时推送（所有传输方式，Android 修改时）
├── 触发：用户在 EspansoGo 中保�?match
├── 逻辑：序列化 YAML �?Push() �?写入同步�?
└── 延迟�? 1 秒（WebDAV PUT / SAF 写入�?
```

### 5. 设置向导 UI

```
WizardStep 1: "你已经在使用桌面 espanso 吗？"
  └─ �?�?Step 2 | �?�?空白开�?/ 包商�?

WizardStep 2: "选择同步方式"
  ├─ �?云文件夹同步（推荐）�?Step 3a
  ├─ 🔄 Syncthing 同步      �?Step 3b
  ├─ 🌐 WebDAV 同步（进阶） �?Step 3c
  ├─ 📦 Git 仓库同步        �?Step 3d
  ├─ 📡 局域网同步           �?Step 3e
  └─ 📄 手动导入             �?Step 3f

WizardStep 3a: "选择云存储中�?espanso match 文件�?
  └─ SAF 文件夹选择�?�?保存 URI �?Step 4

WizardStep 3b: "Syncthing 同步设置"
  ├─ 检测是否已安装 Syncthing app
  �?  └─ 未安�?�?引导安装（Google Play / F-Droid 链接�?
  └─ SAF 选择 Syncthing 同步文件�?�?保存 URI �?Step 4

WizardStep 3c: "输入 WebDAV 服务器信�?
  └─ URL + 用户�?+ 密码 + 远程路径 �?测试连接 �?Step 4

WizardStep 3d: "输入 Git 仓库信息"
  └─ URL + PAT �?clone �?Step 4

WizardStep 3e: "扫描桌面二维�?
  └─ 相机扫码 �?连接 �?Step 4

WizardStep 3f: "选择 YAML 配置文件"
  └─ 文件选择�?�?导入 �?完成

WizardStep 4: "同步�?.." �?"导入完成：N �?matches，M �?global_vars"
  └─ 完成 �?进入主界�?
```

**同步设置页面（日常管理）**�?
```
SyncSettingsPage
├── 当前同步方式：[显示当前方式 + 状态图标]
├── 上次同步：[时间] [立即同步按钮]
├── 同步间隔：[前台轮询间隔设置（WebDAV 专用）]
├── 同步状态详情：
�?  ├── 已同步文件数：N
�?  ├── 冲突文件数：M（如有，点击查看详情�?
�?  └── 快照大小：~XKB
├── 切换同步方式 �?重新运行设置向导
├── 高级�?
�?  ├── 冲突处理策略：[LWW（默认）/ 保留两份]
�?  ├── 清除快照缓存
�?  └── 导出同步日志（用于问题排查）
└── 桌面端配置指�?�?显示对应传输方式的桌面端设置步骤
```

### 6. 桌面端配置指�?

无需开发任何桌面端工具，用户只需选择一种方式：

#### 云文件夹方式（推荐，最简单）

```bash
# Linux / macOS：将 espanso match 目录 symlink 到云同步文件�?
ln -s ~/Google\ Drive/espanso-match/ ~/.config/espanso/match
# 或在云客户端中添�?~/.config/espanso/match/ 为同步文件夹

# Windows：在云客户端中直接添�?match 目录为同步文件夹
# Google Drive / OneDrive / Dropbox 客户�?�?选择文件�?�?添加 %APPDATA%\espanso\match
```

#### Syncthing 方式（无云依赖，实时同步�?

```bash
# 1. 安装 Syncthing
#    macOS: brew install syncthing
#    Linux: sudo apt install syncthing
#    Windows: choco install syncthing

# 2. 启动 Syncthing，打开 Web UI（默�?http://localhost:8384�?
# 3. 添加共享文件夹：
#    文件夹路径：~/.config/espanso/match（或 %APPDATA%\espanso\match�?
#    文件�?ID：espanso-match
# 4. �?Android Syncthing app 中添加同一文件夹，配对设备
```

#### WebDAV 方式（进阶，实时性最好）

**Linux / macOS**�?

方式 1 �?rclone 挂载（推荐）�?
```bash
rclone mount webdav:espanso-match ~/.config/espanso/match --vfs-cache-mode writes --daemon
```

方式 2 �?davfs2（Linux 原生，仅 Linux）：
```bash
mount -t davfs https://nas.local/dav/espanso-match ~/.config/espanso/match
```

**Windows**�?

方式 1 �?rclone 挂载（需 WinFsp，适合有管理员权限的机器）�?
```powershell
# 安装 rclone �?WinFsp
choco install rclone winfsp

# 挂载为盘�?
rclone mount webdav:espanso-match E: --vfs-cache-mode writes
```

方式 2 �?rclone sync + 计划任务（非挂载，适合受限环境）：
```powershell
# 安装 rclone（不需�?WinFsp�?
choco install rclone

# 配置 WebDAV 远程
rclone config  # 选择 webdav，输�?OpenList URL + 凭据

# 创建同步脚本 espanso-sync.ps1
$remote = "webdav:espanso-match"
$local = "$env:APPDATA\espanso\match"

# 拉取远程变更
rclone sync $remote $local --exclude "*.tmp"
# 推送本地变�?
rclone sync $local $remote --exclude "*.tmp"

# 注册�?Windows 计划任务（每 30 秒运行）
$action = New-ScheduledTaskAction -Execute "powershell.exe" `
  -Argument "-WindowStyle Hidden -File C:\Scripts\espanso-sync.ps1"
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date) `
  -RepetitionInterval (New-TimeSpan -Seconds 30)
Register-ScheduledTask -TaskName "EspansoWebDAVSync" `
  -Action $action -Trigger $trigger
```

方式 3 �?文件监控 + 即时同步（事件驱动，推荐用于受限环境）：

通过文件系统监控替代轮询，文件变化时立即触发同步，延�?< 1 秒�?

**Windows**（PowerShell `FileSystemWatcher`�?NET 内置）：
```powershell
# espanso-watch-sync.ps1 �?常驻后台，文件变化时立即 rclone sync
$local = "$env:APPDATA\espanso\match"
$remote = "webdav:espanso-match"

$watcher = New-Object System.IO.FileSystemWatcher $local
$watcher.IncludeSubdirectories = $false
$watcher.EnableRaisingEvents = $true

# 防抖：短时间内多次变化只同步一�?
$lastSync = [DateTime]::MinValue
function SyncNow {
    $now = Get-Date
    if (($now - $lastSync).TotalMilliseconds -gt 500) {
        rclone sync $local $remote --exclude "*.tmp"
        rclone sync $remote $local --exclude "*.tmp"
        $lastSync = $now
    }
}

Register-ObjectEvent $watcher "Changed" -Action { SyncNow }
Register-ObjectEvent $watcher "Created" -Action { SyncNow }
Register-ObjectEvent $watcher "Deleted" -Action { SyncNow }

# 同时定时拉取远程变更�?0s，防止桌面端未感知的远程变化�?
while ($true) {
    Start-Sleep 30
    rclone sync $remote $local --exclude "*.tmp"
}
```

注册为开机自启（不需要管理员权限）：
```powershell
# 放入 shell:startup 文件�?
$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut(
    "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\espanso-sync.lnk")
$shortcut.TargetPath = "powershell.exe"
$shortcut.Arguments = "-WindowStyle Hidden -File C:\Scripts\espanso-watch-sync.ps1"
$shortcut.Save()
```

**Linux**（inotifywait，内核级 inotify 机制）：
```bash
# 安装 inotify-tools
sudo apt install inotify-tools

# 创建监控脚本 espanso-watch-sync.sh
#!/bin/bash
LOCAL="$HOME/.config/espanso/match"
REMOTE="webdav:espanso-match"

# 后台定时拉取远程变更�?0s�?
while true; do
    rclone sync $REMOTE $LOCAL --exclude "*.tmp"
    sleep 30
done &

# 监控本地文件变化，立即推�?
inotifywait -m -e modify,create,delete "$LOCAL" |
  while read path event file; do
      # 防抖�?00ms 内多次变化只同步一�?
      sleep 0.5
      rclone sync $LOCAL $REMOTE --exclude "*.tmp"
  done
```

注册�?systemd 用户服务（不需�?root）：
```ini
# ~/.config/systemd/user/espanso-sync.service
[Unit]
Description=Espanso WebDAV Sync

[Service]
ExecStart=/home/user/scripts/espanso-watch-sync.sh
Restart=always

[Install]
WantedBy=default.target
```
```bash
systemctl --user enable --now espanso-sync
```

**macOS**（fswatch，类�?inotify）：
```bash
brew install fswatch
fswatch -o ~/.config/espanso/match | while read; do
    rclone sync ~/.config/espanso/match webdav:espanso-match
done
```

方式 4 �?�?PowerShell 脚本（零安装，适合完全受限环境）：
```powershell
# espanso-webdav-sync.ps1 �?无需安装任何工具
$baseUrl = "https://nas.local:5244/dav/espanso-match"
$user = "youruser"
$pass = "yourpass"
$local = "$env:APPDATA\espanso\match"
$headers = @{ Authorization = "Basic " + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("$user`:$pass")) }

# 拉取：PROPFIND 获取文件列表，下载变化的文件
$resp = Invoke-WebRequest -Uri $baseUrl -Method PROPFIND -Headers $headers -SkipCertificateCheck
# 解析 XML 获取文件名和 ETag，对比本地缓存，下载变化的文�?..
# （完整脚本约 50 行，此处省略 XML 解析部分�?

# 推送：上传本地修改的文�?
Get-ChildItem "$local\*.yml" | ForEach-Object {
    Invoke-WebRequest -Uri "$baseUrl/$($_.Name)" -Method PUT -Headers $headers `
      -InFile $_.FullName -SkipCertificateCheck
}
```

**受限环境特别说明**�?
- 优先使用方式 3（文件监控），本地修�?< 1s 即同步到远程，同�?30s 定时拉取远程变更
- 方式 2（计划任务）和方�?4（纯脚本）更简单但延迟 30s
- 均不需�?WinFsp / 管理员权�?
- 确认桌面环境�?WebDAV 服务器的网络连通性（�?WebDAV 在本�?NAS，需通过公网 IP �?VPN 访问�?
- espanso �?`auto_restart` 会检�?match 目录下的文件变化，同步后自动重载

**桌面端方案对�?*�?

| 方式 | 平台 | 需要安�?| 需要管理员 | 本地→远程延�?| 适合场景 |
|------|------|---------|-----------|--------------|---------|
| rclone mount | 全平�?| rclone + WinFsp | 是（Windows�?| 实时 | 个人电脑，有完整权限 |
| 文件监控 + rclone sync | 全平�?| rclone + 平台监控工具¹ | �?| < 1s | 受限环境，推�?|
| rclone sync + 计划任务 | 全平�?| �?rclone | �?| 30s | 受限环境，最简�?|
| �?PowerShell 脚本 | Windows | �?| �?| 30s | 完全受限环境 |
| davfs2 | Linux | davfs2 | �?| 实时 | Linux 服务�?桌面 |

> ¹ 平台监控工具：Windows �?`FileSystemWatcher`（PowerShell/.NET 内置，无需安装）；Linux �?`inotifywait`（`apt install inotify-tools`）；macOS �?`fswatch`（`brew install fswatch`）�?

**Git 方式**�?
```bash
cd ~/.config/espanso/match
git init && git remote add origin <repo-url>
# 可选自动同步脚本：
echo '* * * * * cd ~/.config/espanso/match && git add -A && git commit -m "auto" && git push' | crontab
```

---

## 实施路线�?

### 第零期：WebDAV PROPFIND 原型验证（风险消除）

| 任务 | 改动文件 | 优先�?|
|------|---------|--------|
| `HttpClient` 自定�?method PoC（PROPFIND/MKCOL�?| 临时测试项目 | �?|
| `AndroidClientHandler` 兼容性验�?| 临时测试项目 | �?|
| XML PROPFIND 响应解析验证 | 临时测试项目 | �?|

> **目标**：在正式开发前验证 WebDAV �?.NET MAUI Android 上的技术可行性。如�?`AndroidClientHandler` 不支持自定义 method，需提前确定 fallback 方案（OkHttp Binding �?SocketsHttpHandler）�?

### 第一期：基础设施 + CloudFolder + Syncthing 同步（核心）

| 任务 | 改动文件 | 优先�?|
|------|---------|--------|
| `YamlWorkspace` 服务（含未知字段保留 + config �?glob + match 层逐文�?imports�?| 新建 `Services/YamlWorkspace.cs` | �?|
| `ConfigSyncExtractor`（提�?config �?match 相关字段，含 excludes/extra_excludes�?| `YamlWorkspace.cs` | �?|
| glob 匹配实现（手动遍�?+ `_` 前缀过滤，不依赖 FileSystemGlobbing�?| `YamlWorkspace.cs` | �?|
| `SyncManager` 服务（含即时推�?+ 智能轮询 + LWW 冲突策略�?| 新建 `Services/SyncManager.cs` | �?|
| SAF 持久�?URI 管理 + hash 变化检�?| `AppSettings.cs` + 新建 `Services/SafManager.cs` | �?|
| 前台 ContentObserver + 短轮�?fallback | 新建 `Platforms/Android/Services/SafObserver.cs` | �?|
| WorkManager 后台同步 | 新建 `Platforms/Android/Workers/SyncWorker.cs` | �?|
| 同步状态持久化（`.EspansoGo-sync.json`�?| `SyncManager.cs` | �?|
| 设置向导 UI（含 Syncthing 引导�?| 新建 `Pages/SyncSetupWizard.razor` | �?|
| 同步设置页面（日常管理） | 新建 `Pages/SyncSettings.razor` | �?|
| 同步状态指示器（主界面顶部 + 立即同步按钮�?| `Shared/SyncStatus.razor` | �?|
| 批量 YAML 导入（替代当前单文件�?| 重构 `Index.razor` ImportAsync | �?|
| 冲突保留两份 UI | 新建 `Pages/ConflictResolver.razor` | �?|

> CloudFolder �?Syncthing 共享 SAF 读写代码，实现成本接近一个方案�?
> 第一期使�?LWW + 冲突保留两份，不实现三方合并，降低首期复杂度，快速验证核心链路�?

### 第二期：WebDAV 同步 + Espanso Hub 客户�?

| 任务 | 改动文件 | 优先�?|
|------|---------|--------|
| WebDAV 客户端（PROPFIND/GET/PUT/MKCOL/DELETE�?| 新建 `Services/WebDavClient.cs` | �?|
| 前台短轮询服务（WebDAV 30s 轮询�?| 新建 `Platforms/Android/Services/PollingService.cs` | �?|
| WebDAV 兼容性测试（Nextcloud / 坚果�?/ Synology�?| 新建 `Tests/WebDavCompatTest.cs` | �?|
| Hub API 客户�?| 新建 `Services/HubClient.cs` | �?|
| 包索引缓�?| `HubClient.cs` | �?|
| 包浏�?UI | 新建 `Pages/PackageStore.razor` | �?|
| 包安装（下载 + 校验 + 解压�?| `HubClient.cs` | �?|
| 已安装包管理 | 新建 `Models/InstalledPackage.cs` | �?|

### 第三期：三方合并 + Git 同步（可选）

| 任务 | 改动文件 | 优先�?|
|------|---------|--------|
| 快照管理（三方合并基础�?| 新建 `Services/SnapshotManager.cs` | �?|
| YAML 结构感知三方合并（trigger 级整体替换，不做字段级合并） | `SyncManager.cs` | �?|
| Termux git 集成（优先）�?libgit2sharp | `EspansoGo.csproj` + `Services/GitSyncService.cs` | �?|
| PAT 认证管理 | `Services/CredentialManager.cs` | �?|
| Git 同步 UI | 扩展设置向导 | �?|
| libgit2sharp Android ABI 兼容性验�?| 测试 | �?|

### 第四期：局域网同步（可选，ROI 低）

> **降级说明**：Syncthing 已覆盖局域网 P2P 同步场景，LocalNetwork 方案需开发桌�?companion 工具，ROI 低。仅在社区有强烈需求时实现�?

| 任务 | 改动文件 | 优先�?|
|------|---------|--------|
| 桌面�?companion 工具 | 独立项目 `espanso-sync` | �?|
| mDNS 发现 | `Services/MdnsDiscovery.cs` | �?|
| 二维码配�?| `Pages/PairingPage.razor` | �?|
| HTTP 同步客户�?| `Services/LocalSyncService.cs` | �?|

> **优先级调整说�?*�?
> 1. 新增第零期：WebDAV PROPFIND 兼容性是高风险项，需早期原型验证
> 2. 第一期简化冲突策略：LWW + 保留两份，三方合并推迟到第三�?
> 3. 第一期增�?ContentObserver + 同步状态持久化 + packages YAML 同步
> 4. 第四�?LocalNetwork 降级为可选，Syncthing 已覆盖局域网场景

---

## 与兼容性改进计划的协同

本同步方案与 `ESPANSO_COMPATIBILITY_PLAN.md` 中的改进相互依赖�?

| 兼容性改�?| 同步方案依赖 | 说明 |
|-----------|-------------|------|
| `triggers` 多触发词 | YamlWorkspace | 同步时需正确处理多触发词 |
| `imports` 跨文件引�?| YamlWorkspace | 同步时需递归解析 |
| 日期格式转换 | YamlWorkspace | YAML 中存 chrono 格式，内存中�?.NET 格式 |
| `left_word`/`right_word` | YamlWorkspace | YAML 字段需正确序列�?|
| `choice` 变量类型 | Hub 客户�?| hub 包中可能包含 choice 变量 |
| `propagate_case` | YamlWorkspace | YAML 字段需正确序列�?|

**建议实施顺序**：先完成兼容性改进的阶段一（模型扩展），再开始同步方案的第一期。这�?YamlWorkspace 在序列化/反序列化时就能直接支持新字段�?

---

## 不在计划范围�?

| 特�?| 原因 |
|------|------|
| `shell` / `script` 变量同步 | Android 无桌�?shell，同步后也无法执行（�?YAML 中保留字段不丢失�?|
| `image_path` 同步 | AccessibilityService 不支持图片注入（�?YAML 中保留字段不丢失�?|
| `config/default.yml` 整体同步 | 桌面端配置（backend, toggle_key 等）�?Android 无关；仅同步 `includes`/`use_standard_includes`/`match_paths` 字段 |
| `filter_title` / `filter_exec` 同步 | Android �?packageName 机制，与桌面不同（但 YAML 中保留字段不丢失�?|
| `packages/` 目录同步 | 两端独立安装包，仅同�?`match/` 目录（见下方 packages 同步策略�?|
| 实时同步�? 10 秒） | WebDAV 短轮询最�?10s，Syncthing P2P 可达秒级；更短需前台常驻服务（耗电�?|
| 远程触发扩展 | 属于 LocalNetwork 方案的扩展功能，第四期考虑 |

> **重要原则**：不同步的特性不代表丢弃�?YAML 字段。YamlWorkspace 使用 `Dictionary<string, object>` 中间表示，所有未知字段在序列化时原样写回，确保双向同步不丢失任何数据。运行时模型（`Dict<string, Match>`）只解析 EspansoGo 支持的字段用于扩展执行�?

### Packages 同步策略

**策略：两端独立安装包，仅同步 `match/` 目录**

> **关键事实**：espanso �?`STANDARD_INCLUDES`（`["../match/**/[!_]*.yml", "../match/**/[!_]*.yaml"]`）路径相对于 `config/` 目录，解析后指向 `match/` 目录�?*不覆�?`packages/` 目录**。packages 的加载是通过独立的包机制（`packages/` 目录 + `package.yml` 清单），不是通过 `STANDARD_INCLUDES` glob 匹配�?
>
> 因此同步方案只需负责 `match/` 目录�?YAML 文件，packages 由两端各自独立安装�?

| 方面 | 说明 |
|------|------|
| 安装方式 | 桌面�?`espanso package install`，Android �?Hub 客户端安�?|
| 同步范围 | 仅同�?`match/` 目录，不同步 `packages/` 目录 |
| 版本一致�?| 不强制版本一致；用户可在两端安装不同版本的包 |
| 包内 global_vars 引用 | 如果 `match/` 中的文件引用了包内定义的 `global_vars`，需在对应端也安装该包，否则变量解析失败（UI 提示缺失依赖�?|

**为什么不同步 packages 目录**�?
- packages 路径在不同平台不同（桌面 `packages/`，Android 内部存储），跨平台路径映射复�?
- packages 可能包含二进制资源（图标等），增加同步流�?
- espanso 的包加载是独立机制（`packages/` + `package.yml` 清单），不通过 `STANDARD_INCLUDES` 发现
- 两端独立安装更灵活，用户可以�?Android 上只安装需要的�?
- Hub 客户端已提供便捷的包安装方式，无需通过同步复制
