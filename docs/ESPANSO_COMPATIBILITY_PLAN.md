# Expandroid ↔ Espanso 兼容性改进实施计划

## 项目背景

Expandroid 是 Android 端的 espanso 兼容文本扩展器，通过 Android AccessibilityService 实现文本检测与替换。本计划旨在提升 Expandroid 与 espanso 桌面版配置的双向兼容性，使用户能在桌面和移动端无缝共享配置。

**当前技术栈**：Kotlin + Jetpack Compose + Jackson YAML/JSON + kotlinx.serialization  
**Espanso 技术栈**：Rust workspace（15 个 crate），YAML 配置，JSON Schema 校验  
**目标**：最大化配置兼容性，同时尊重 Android 平台限制

---

## 现状分析

### 已支持的 Espanso 特性

| 特性 | 状态 | 说明 |
|------|------|------|
| `trigger` (单个) | ✅ | 完全支持 |
| `replace` | ✅ | 完全支持 |
| `vars` — `echo` | ✅ | 完全支持 |
| `vars` — `date` | ✅ 大部分 | 格式转换 `getTheRealFormat`，覆盖完整；`%j`/`%w`/`%C`/`%G`/`%V`/`%Z`/`%s` 直接求值 |
| `vars` — `choice` | ✅ | 弹出选择 UI，支持 `values` 字段（字符串或数组） |
| `vars` — `clipboard` | ✅ | 完全支持 |
| `vars` — `random` | ✅ | 完全支持 |
| `form` + `form_fields` | ✅ 部分 | 支持 text/choice/list 字段，缺少 multiline |
| `global_vars` | ✅ | 完全支持 |
| `word` | ✅ | 完全支持 |

### 未支持的 Espanso 特性

| 特性 | 影响程度 | 平台限制 | 说明 |
|------|----------|----------|------|
| `triggers` (多触发词) | ✅ | 导入时展开为多个 Match 条目 |
| `imports` (跨文件引用) | ⚠️ 限制 | ConfigImportReceiver 通过 Intent 接收单文件，无文件路径上下文；MainViewModel 导入同理 |
| `left_word` / `right_word` | ✅ | 精细词边界控制已实现 |
| `choice` 变量类型 | ✅ | 独立变量弹出选择 UI |
| `propagate_case` / `uppercase_style` | ✅ | 大小写传播已实现 |
| `regex` 触发器 | ✅ | 使用 `java.util.regex.Pattern` 匹配，存储在 `regexDict` |
| `markdown` / `html` | 低 | 无 | 替换格式变体 |
| `image_path` | 低 | 平台限制 | Android 无障碍服务不支持图片注入 |
| `shell` / `script` 变量 | 低 | 平台限制 | Android 无桌面 shell 环境 |
| `label` / `search_terms` | 低 | 无 | 搜索 UI 相关，当前无搜索功能 |
| `force_clipboard` / `force_mode` | 低 | 平台限制 | Android 固定用 SetText，无注入模式选择 |
| `filter_title` / `filter_exec` | 低 | 平台限制 | App 特定配置，Android 可通过 packageName 实现 |
| `paragraph` | 低 | 无 | markdown 段落控制 |

### 日期格式转换问题

`Utils.getTheRealFormat` 已修复以下缺陷：

1. ~~**替换顺序问题**~~：已使用 token 化占位符方式避免级联替换
2. ~~**缺少格式说明符**~~：已补充 `%N`（纳秒→`SSSSSSS`）、`%z`（时区偏移→`XXX`）、`%Z`（时区名→直接求值）、`%C`（世纪→直接求值）、`%G`/`%V`（ISO 周日期→直接求值）、`%s`（Unix 时间戳→直接求值）
3. **`%j` 和 `%w` 直接求值**：仍为直接求值（Java `DateTimeFormatter` 无直接等价格式说明符），导入时固定为当前值
4. `getOriginalFormat` 同样使用 token 化方式，按字符串长度从长到短替换；`XXX`→`%z`、`SSSSSSS`→`%N` 已添加

---

## 实施计划

> **实施状态**：所有阶段均已实现。以下标注各任务的实际完成情况及对应 Kotlin 文件。

### 阶段一：模型扩展与导入改进（高优先级）

#### 1.1 扩展 `Match` 模型支持 `triggers` 多触发词 ✅ 已完成

**目标**：导入 espanso YAML 时，`triggers: [":a", ":b"]` 能正确展开为多个条目。

**改动文件**：  
- `app/src/main/java/.../data/Models.kt` — `Match` 包含 `triggers` 字段 ✅  
- `app/src/main/java/.../service/ConfigImportReceiver.kt` — 导入时将 `triggers` 展开为多个 Match ✅  
- `app/src/main/java/.../ui/MainViewModel.kt` — 导入逻辑同步处理 ✅

**实施方案**：  
```kotlin
// Models.kt — Match data class
var triggers: MutableList<String>? = null

// ConfigImportReceiver.kt / MainViewModel.kt — 导入时
if (!match.triggers.isNullOrEmpty()) {
    match.triggers!!.forEach { t ->
        val cloned = Match(match)
        cloned.trigger = t
        cloned.triggers = null
        dict[t] = cloned
    }
}
```

**验证**：导入包含 `triggers` 的 YAML 文件，确认每个触发词都能正确触发扩展。

#### 1.2 支持 `imports` 跨文件引用 ⚠️ 平台限制

**目标**：导入 espanso 配置时，递归解析 `imports` 字段引用的其他 YAML 文件。

**改动文件**：  
- `app/src/main/java/.../service/ConfigImportReceiver.kt` — ⚠️ 通过 Intent 字符串接收配置，无文件路径上下文，属合理限制  
- `app/src/main/java/.../ui/MainViewModel.kt` — 通过 SAF (Storage Access Framework) 选择文件，同样无目录上下文  

**限制说明**：Android 的文件选择器返回的是 content URI，无法获取同目录下的其他文件路径。如需支持 imports，需要用户选择整个目录（`ACTION_OPEN_DOCUMENT_TREE`），但这显著增加 UX 复杂度。当前设计为单文件导入。

#### 1.3 完善 `left_word` / `right_word` 支持 ✅ 已完成

**目标**：支持 espanso 的精细词边界匹配。

**改动文件**：  
- `app/src/main/java/.../data/Models.kt` — `Match` 包含 `leftWord` / `rightWord` 字段 ✅  
- `app/src/main/java/.../service/ExpanderAccessibilityService.kt` — `handleTextExpansion` 中的词边界检查 ✅

**实施方案**：  
```kotlin
// Models.kt — Match data class
var leftWord: Boolean = false
var rightWord: Boolean = false

// ExpanderAccessibilityService.kt — handleTextExpansion
val checkLeft = match.word || match.leftWord
val checkRight = match.word || match.rightWord
if (checkLeft || checkRight) {
    val beforeOk = triggerIndex == 0 ||
        SEPARATORS.contains(expansionStr[triggerIndex - 1].toString())
    val afterOk = triggerIndex + text.length >= expansionStr.length ||
        SEPARATORS.contains(expansionStr[triggerIndex + text.length].toString())
    if (checkLeft && !beforeOk) return
    if (checkRight && !afterOk) return
}
```

**验证**：测试 `:hello` 在 `say :hello world` 中触发（right_word=true），在 `say:hello` 中不触发。

---

### 阶段二：变量类型扩展（中优先级）

#### 2.1 支持 `choice` 变量类型 ✅ 已完成

**目标**：独立 `choice` 变量类型弹出选择列表，复用现有 form 的 choice UI 逻辑。

**改动文件**：  
- `app/src/main/java/.../data/AppSettings.kt` — `choice` 在 `supportedList` 中 ✅  
- `app/src/main/java/.../data/Models.kt` — `Params` 包含 `values` 字段 ✅  
- `app/src/main/java/.../data/SerializationHelper.kt` — 自定义反序列化器 `ValuesStringOrArrayDeserializer`，支持字符串或数组 ✅  
- `app/src/main/java/.../service/ExpanderAccessibilityService.kt` — `parseItem` 中 `choice` case 弹出 `showChoiceSelection` 浮动窗口 ✅

**实施方案**：
- `choice` 变量的 `params.values` 可以是换行分隔的字符串或数组
- 触发时弹出浮动选择窗口（复用现有 form 的 Spinner/ListView UI）
- 用户选择后将值替换到 `{{varname}}` 占位符

**UI 流程**：
1. 检测到 `choice` 变量 → 暂停扩展
2. 显示浮动窗口 + 选项列表
3. 用户选择 → 继续后续变量解析 → 完成扩展

#### 2.2 修复日期格式转换 ✅ 已完成

**目标**：`GetTheRealFormat` 正确覆盖所有常用 chrono 格式说明符，且替换顺序不产生冲突。

**改动文件**：  
- `app/src/main/java/.../util/Utils.kt` — `getTheRealFormat` 和 `getOriginalFormat` 使用 token 化替换 ✅

**实施方案**：
- 使用 token 化方式而非简单字符串替换：先用占位符标记所有 `%X` 模式，再逐个替换为 Java `DateTimeFormatter` 等价格式 ✅  
- 补充缺失的格式说明符：`%C`（世纪）、`%G`/`%V`（ISO 周日期）、`%Z`（时区名）、`%z`（偏移→`XXX`）、`%N`（纳秒→`SSSSSSS`）、`%s`（Unix 时间戳）✅  
- `%j` 和 `%w`：仍为直接求值（Java `DateTimeFormatter` 无直接等价） ✅  
- `getOriginalFormat` 同样使用 token 化方式，按字符串长度从长到短替换 ✅

**补充格式映射表**：

| chrono | .NET | 说明 |
|--------|------|------|
| `%Y` | `yyyy` | 4位年份 |
| `%y` | `yy` | 2位年份（**当前缺失**） |
| `%m` | `MM` | 2位月份 |
| `%b` | `MMM` | 月份缩写 |
| `%B` | `MMMM` | 月份全名 |
| `%d` | `dd` | 2位日期 |
| `%e` | `d` | 日期（不补零） |
| `%a` | `ddd` | 星期缩写 |
| `%A` | `dddd` | 星期全名 |
| `%H` | `HH` | 24小时制 |
| `%I` | `hh` | 12小时制 |
| `%p` | `tt` | AM/PM |
| `%M` | `mm` | 分钟 |
| `%S` | `ss` | 秒 |
| `%y` | `yy` | 2位年份（**已添加**） |
| `%n` | `\n` | 换行（**已添加**） |
| `%t` | `\t` | 制表符（**已添加**） |
| `%%` | `%` | 百分号字面量（**已添加**） |
| `%N` | `fffffff` | 纳秒（**已添加**） |
| `%z` | `zzz` | 时区偏移（**已添加**） |
| `%Z` | 时区名 | 时区名称（**已添加**，直接求值） |
| `%C` | 世纪 | 世纪数（**已添加**，直接求值） |
| `%G` | ISO 年 | ISO 8601 年（**已添加**，直接求值） |
| `%V` | ISO 周 | ISO 8601 周数（**已添加**，直接求值） |
| `%u` | ISO 星期 | ISO 1-7（**已添加**，直接求值） |

---

### 阶段三：大小写传播（中优先级）

#### 3.1 实现 `propagate_case` / `uppercase_style` ✅ 已完成

**目标**：当触发词全大写或首字母大写时，替换文本也相应变换大小写。

**改动文件**：  
- `app/src/main/java/.../data/Models.kt` — `Match` 包含 `propagateCase` / `uppercaseStyle` 字段 ✅  
- `app/src/main/java/.../service/ExpanderAccessibilityService.kt` — `handleTextExpansion` 中替换前应用 `applyPropagateCase` ✅

**实施方案**：  
```kotlin
// Models.kt — Match data class
var propagateCase: Boolean = false
var uppercaseStyle: String? = null  // "uppercase" | "capitalize" | "capitalize_words"

// ExpanderAccessibilityService.kt — handleTextExpansion
if (match.propagateCase) {
    replace = applyPropagateCase(text, replace, match.uppercaseStyle)
}
```

**验证**：`:hello` → "Hi there!"，`:HELLO` → "HI THERE!"，`:Hello` → "Hi There!"。

---

### 阶段四：导出改进与双向兼容（低优先级）

#### 4.1 导出为 espanso 兼容 YAML ✅ 已完成

**目标**：导出的 YAML 文件能被 espanso 桌面版直接导入使用。

**改动文件**：  
- `app/src/main/java/.../ui/MainViewModel.kt` — `exportConfig` 方法，使用 `SerializationHelper.toYaml` + `getOriginalFormat` ✅  
- `app/src/main/java/.../ui/MainScreen.kt` — 使用 `ActivityResultContracts.CreateDocument` 选择导出路径 ✅

**实施方案**：
- 导出时使用 espanso 的字段命名约定（`snake_case`，已通过 `PropertyNamingStrategies.SNAKE_CASE` 实现）✅  
- 确保导出的 `vars` 中 `type` 字段正确 ✅  
- 日期格式导出时调用 `getOriginalFormat` 转回 chrono 格式 ✅

#### 4.2 导入时保留不支持的字段 ✅ 已完成

**目标**：导入 espanso 配置时，不支持的变量类型（如 `shell`、`script`）的 match 被跳过而非导致错误，同时保留原始信息以便用户参考。

**实施方案**：  
- 在导入日志中记录被跳过的 match 及原因 ✅（`ConfigImportReceiver.kt` 输出 `Log.w`，返回 `skipped` 计数）  
- 在 UI 中显示导入摘要（成功 N 条，跳过 M 条）✅（通过 `snackbarMessage` 显示）

---

### 阶段五：正则触发器（探索性，低优先级）

#### 5.1 支持 `regex` 触发器 ✅ 已完成

**目标**：支持 espanso 的正则表达式触发器。

**改动文件**：  
- `app/src/main/java/.../data/Models.kt` — `Match` 包含 `regex` 字段 ✅  
- `app/src/main/java/.../service/ExpanderAccessibilityService.kt` — `regexDict: MutableMap<Pattern, Match>`，在 `handleTextExpansion` 中添加正则匹配逻辑 ✅  
- `app/src/main/java/.../service/ConfigImportReceiver.kt` — 导入时处理 regex-only match ✅

**实施方案**：  
- 存储 regex 触发的 match 在单独的 `MutableMap<Pattern, Match>` 中  
- 在 `handleTextExpansion` 中，当 dict 精确匹配失败后，遍历 `regexDict` 对 `expansionStr` 进行正则匹配  
- 匹配成功后，用匹配文本替换为 `replace` 内容  
- 支持 `propagateCase` 和 `vars` 处理

**限制**：
- AccessibilityService 只能获取完整文本，无法像 espanso 那样逐键检测
- 正则匹配性能需关注，建议限制正则复杂度
- `max_regex_buffer_size` 配置可用于限制匹配文本长度

---

## 实施优先级与时间线

| 阶段 | 任务 | 优先级 | 预计改动量 | 依赖 |
|------|------|--------|-----------|------|
| 1.1 | `triggers` 多触发词 | 高 | 小 | 无 |
| 1.2 | `imports` 跨文件引用 | 高 | 中 | 无 |
| 1.3 | `left_word` / `right_word` | 高 | 小 | 无 |
| 2.1 | `choice` 变量类型 | 中 | 中 | 无 |
| 2.2 | 日期格式转换修复 | 中 | 小 | 无 |
| 3.1 | `propagate_case` / `uppercase_style` | 中 | 中 | 1.1 |
| 4.1 | 导出为 espanso 兼容 YAML | 低 | 小 | 2.2 |
| 4.2 | 导入时保留不支持的字段 | 低 | 小 | 无 |
| 5.1 | `regex` 触发器 | 低 | 大 | 1.1, 1.3 |

---

## 验证策略

### 单元测试 ❌ 未实现  
- `Utils.getTheRealFormat` / `getOriginalFormat`：覆盖所有 chrono 格式说明符的往返测试  
- `Models.kt` YAML 序列化/反序列化：使用 espanso 示例配置文件验证  

### 集成测试 ❌ 未实现  
- 导入 espanso 官方示例配置验证兼容性  
- 导入包含 `triggers`、`vars`（各类型）、`regex` 的完整配置文件  
- 导出 YAML 后用 espanso 的 JSON Schema（`schemas/match.schema.json`）验证  

### 手动测试  
- 在 Android 设备上测试各变量类型的实际扩展行为  
- 测试 `propagate_case` 在不同大小写触发词下的行为  
- 测试 `left_word` / `right_word` 在各种文本上下文中的触发准确性  
- 测试 `regex` 触发器匹配  
- 测试 `choice` 变量弹出选择 UI  
- 测试 YAML 导入/导出往返兼容性

---

## 不在计划范围内

以下特性因 Android 平台限制或投入产出比过低，明确不在本次计划范围内：

- **`shell` / `script` 变量**：Android 无桌面 shell 环境，且安全限制使得执行任意脚本不现实
- **`image_path`**：AccessibilityService 的 `SetText` action 不支持图片注入
- **`force_clipboard` / `force_mode`**：Android 固定使用 `SetText`，无注入模式选择
- **`filter_title` / `filter_exec`**：可通过 `packageName` 实现 App 特定配置，但当前需求不足
- **`backend` / `paste_shortcut` 等桌面端配置**：与 Android 实现机制无关
