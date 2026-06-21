# Expandroid UI 优化方案

## 项目背景

Expandroid 是一个 Android 端的 espanso 兼容文本扩展器，用户定义触发词自动替换为预设文本，支持日期/时间变量、剪贴板、随机数等扩展，另附文本工具（比较、格式化、Discord Markdown）。

**技术栈**：.NET 9 MAUI + Blazor WebView + MudBlazor 6.19.1 + YamlDotNet  
**平台**：Android 7.0+  
**国际化**：中英双语

---

## 当前问题

| # | 问题 | 位置 |
|---|------|------|
| 1 | 硬编码深色模式，无浅色主题 | `MainLayout.razor:3` `IsDarkMode="true"` |
| 2 | 主题配置几乎为空，大量注释代码 | `MainLayout.razor:20-40` |
| 3 | 无导航栏，应用标识缺失 | `MainLayout.razor` |
| 4 | `index.html` 标题还是模板名 `MauiApp1` | `index.html:6` |
| 5 | `app.css` 是 MAUI 模板默认内容，含无用样式 | `wwwroot/css/app.css` |
| 6 | `MainLayout.razor.css` 是模板默认内容，引用不存在的元素 | `MainLayout.razor.css` |
| 7 | `App.xaml` 原生样式与 MudBlazor 主题脱节 | `App.xaml:9-21` |
| 8 | Splash 颜色 `#D1F00F` 与应用主题不协调 | `Expandroid.csproj:51` |
| 9 | `Index.razor` 785 行单文件，可维护性差 | `Pages/Index.razor` |
| 10 | 操作反馈用确认对话框（`DisplayAlert`），体验生硬 | `DialogService.cs` |
| 11 | 按钮分散无分组，操作流程不清晰 | `Index.razor:28-35` |
| 12 | 列表项样式简陋，缺少视觉层次 | `Index.razor:114-128` |

---

## 优化方案

### 1. 主题系统

#### 配色方案

**浅色模式**
| 角色 | 颜色 | 用途 |
|------|------|------|
| Primary | `#6366f1` (靛蓝) | 主按钮、链接、高亮 |
| Secondary | `#8b5cf6` (紫色) | 次要操作 |
| Success | `#10b981` | 保存、成功提示 |
| Error | `#f43f5e` | 删除、错误提示 |
| Background | `#f8fafc` | 页面背景 |
| Surface | `#ffffff` | 卡片背景 |
| Text | `#1e293b` | 正文 |

**深色模式**
| 角色 | 颜色 | 用途 |
|------|------|------|
| Primary | `#818cf8` | 主按钮、链接、高亮 |
| Secondary | `#a78bfa` | 次要操作 |
| Success | `#34d399` | 保存、成功提示 |
| Error | `#fb7185` | 删除、错误提示 |
| Background | `#0f172a` | 页面背景 |
| Surface | `#1e293b` | 卡片背景 |
| Text | `#f1f5f9` | 正文 |

#### 主题服务

```csharp
public interface IThemeService
{
    AppTheme CurrentTheme { get; }
    bool IsDarkMode { get; }
    event Action? OnThemeChanged;
    void SetTheme(AppTheme theme);
    void ApplyTheme();
}

public enum AppTheme { Light, Dark, Auto }
```

- 持久化到 `Preferences`，启动时自动加载
- Auto 模式通过 `Application.Current.RequestedTheme` 跟随系统
- 切换时触发 `OnThemeChanged` 事件，`MainLayout` 响应更新

#### 主题切换 UI

导航栏右侧放置 `MudToggleGroup`，三个选项：浅色 / 深色 / 跟随系统，用图标区分。

### 2. 顶部导航栏

```
[Expandroid]                    [☀️/🌙/🔄] [语言切换]
```

- 使用 `MudAppBar` 实现，`Elevation="2"` 轻微阴影
- 左侧：应用名称（`MudText Typo.H6`）
- 右侧：主题切换 + 语言切换（合并现有 `LanguageSwitcher`）
- 高度 56px，适配 Android 状态栏安全区域

### 3. 模板清理与基础修复

| 文件 | 操作 |
|------|------|
| `index.html` | 标题改为 `Expandroid`，移除 `MauiApp1.styles.css` 引用 |
| `app.css` | 删除模板默认样式（`.btn-primary`、`open-iconic` 等），仅保留 `blazor-error-ui` 和安全区域样式 |
| `MainLayout.razor.css` | 删除模板默认样式（`.sidebar`、`.top-row` 等），替换为实际使用的布局样式 |
| `App.xaml` | 移除 `#263238` 背景、`#2b0b98` 按钮色等硬编码颜色，改为透明或与 MudBlazor 主题一致 |
| `Expandroid.csproj` | Splash 颜色改为主题主色 `#6366f1` |

### 4. 视觉优化

#### 卡片样式

利用 MudBlazor 内置属性，不使用 CSS `!important` 覆盖：

```razor
<MudCard Elevation="2" Class="pa-4 mb-3" Outlined="false">
```

- 列表项从 `MudPaper border-solid border-2` 改为 `MudCard Elevation="2"`
- 统一内边距 `pa-4`、外边距 `mb-3`
- 按钮组使用 `MudButtonGroup` 分组，替代散排按钮

#### 按钮分组

当前 `Index.razor:28-35` 的保存/导入/导出/退出按钮散排，改为：

```razor
<MudStack Row="true" Justify="Justify.Center" Spacing="2" Class="mb-3">
    <MudButtonGroup Variant="Variant.Outlined" Color="Color.Primary">
        <MudButton OnClick="SaveDictAsync" StartIcon="@Icons.Material.Filled.Save">
            @localizationService.GetString("MakeSureToSave")
        </MudButton>
        <MudButton OnClick="ImportAsync" StartIcon="@Icons.Material.Filled.Upload">
            @localizationService.GetString("Import")
        </MudButton>
        <MudButton OnClick="ExportAsync" StartIcon="@Icons.Material.Filled.Download">
            @localizationService.GetString("Export")
        </MudButton>
    </MudButtonGroup>
    <MudButton OnClick="ForceQuit" Color="Color.Error" Variant="Variant.Outlined"
               StartIcon="@Icons.Material.Filled.Close">
        @localizationService.GetString("ForceQuitApp")
    </MudButton>
</MudStack>
```

- 使用 MudBlazor 内置 `Icons.Material.Filled.*` 图标，不引入第三方图标库
- 触摸目标通过 MudBlazor `Size="Size.Large"` 保证 ≥48px

#### 间距规范

统一使用 MudBlazor utility class：
- 卡片间距：`mb-3` (12px)
- 卡片内边距：`pa-4` (16px)
- 按钮组间距：`Spacing="2"` (8px)
- 区块间距：`mt-4` (16px)

### 5. 操作反馈优化

当前所有操作反馈都通过 `DialogService.DisplayConfirmAsync` → `Application.Current.MainPage.DisplayAlert` 弹原生确认框，体验生硬。

**改进**：用已注册但未使用的 `MudSnackbarProvider` 替代非关键反馈：

```csharp
// 成功操作 → Snackbar（自动消失，不打断用户）
Snackbar.Add("Saved successfully", Severity.Success);

// 危险操作确认 → 保留对话框（如删除、导入覆盖）
await dialogService.DisplayConfirmAsync("Warning", "Import will overwrite...", "Proceed", "Cancel");
```

- 保存成功、复制成功、导出成功 → Snackbar
- 导入覆盖确认、删除确认、权限请求 → 保留对话框
- 需要在 `MauiProgram.cs` 注册 `ISnackbarService`（`AddMudServices()` 已包含）

### 6. 组件拆分

将 `Index.razor`（785 行）拆分为：

```
Pages/
  Index.razor              — 主页面，仅包含 Tab 容器
  TextExpander.razor       — 文本扩展器 Tab 内容
  TextTools.razor          — 文本工具 Tab 内容
Shared/
  MatchCard.razor          — 单个匹配规则卡片
  VariableEditor.razor     — 变量编辑器（模板 + 高级选项）
```

- 每个组件通过 `[Parameter]` 接收数据，`[EventCallback]` 回传操作
- 共享状态通过现有 `WeakReferenceMessenger` 传递，不引入新依赖

### 7. 首次使用提示

不做完整引导系统，仅在首次打开时显示一条 `MudAlert`：

```razor
@if (showWelcome)
{
    <MudAlert Severity="Severity.Info" Class="mb-3" OnClose="() => showWelcome = false">
        欢迎使用 Expandroid！添加触发词和替换文本，然后保存即可开始使用。
        可从 espanso 导入现有配置。
    </MudAlert>
}
```

- `showWelcome` 通过 `Preferences.Get("welcomed", false)` 控制
- 关闭后持久化，不再显示

---

## 实施计划

### 阶段 1：基础修复 + 主题系统（1-2 天）

**任务**
1. 清理模板残留（`index.html`、`app.css`、`MainLayout.razor.css`、`App.xaml`）
2. 修复 Splash 颜色
3. 创建 `IThemeService` / `ThemeService`
4. 配置浅色/深色 `MudTheme`
5. 在 `MauiProgram.cs` 注册主题服务
6. 更新 `MainLayout.razor` 使用主题服务

**验收**
- 浅色/深色/自动切换正常
- 主题偏好持久化
- 无模板残留内容

### 阶段 2：导航栏 + 视觉优化（1-2 天）

**任务**
1. 创建 `TopBar.razor`（应用名 + 主题切换 + 语言切换）
2. 更新 `MainLayout.razor` 集成 `TopBar`
3. 列表项改为 `MudCard` 样式
4. 按钮分组 + 图标
5. 统一间距规范

**验收**
- 导航栏显示正确
- 卡片样式统一
- 按钮有图标、分组清晰

### 阶段 3：交互优化 + 组件拆分（1-2 天）

**任务**
1. 引入 `MudSnackbar` 替代非关键对话框
2. 拆分 `Index.razor` 为 `TextExpander.razor` + `TextTools.razor`
3. 提取 `MatchCard.razor` + `VariableEditor.razor`
4. 添加首次使用提示

**验收**
- 保存/导出/复制反馈为 Snackbar
- 删除/导入仍为确认对话框
- 各组件独立可维护
- 首次提示正常显示和关闭

---

## 不做的事情

| 内容 | 原因 |
|------|------|
| Material Design 3 动态色彩 | MudBlazor 6.x 是 MD2 组件库，不兼容 MD3 |
| CSS `!important` 覆盖圆角 | 破坏组件内部样式，用 MudBlazor 内置属性替代 |
| 手势系统（滑动删除、下拉刷新等） | 配置工具场景不需要，Blazor WebView 实现成本高收益低 |
| 性能监控 / 设备检测 / 省电模式 | 本地渲染的配置工具，无性能瓶颈 |
| 用户引导系统（教程、FAQ、视频） | 应用功能简单，一条 Alert 提示足够 |
| Lucide 图标库 | MudBlazor 自带 Material Icons，无需引入第三方 |
| 响应式桌面适配 | 这是 Android 手机应用 |
| 键盘快捷键 | Android 用户基本无硬件键盘 |
| 虚拟滚动 / 代码分割 | 已有懒加载，百级数据量无需虚拟滚动 |
| WCAG 全面合规审计 | 投入产出比低，保证触摸目标和对比度即可 |

---

## 预期效果

- **现代化外观**：双主题 + 导航栏 + 卡片化，视觉层次清晰
- **用户友好**：Snackbar 即时反馈，按钮分组带图标，操作流程清晰
- **代码质量**：组件拆分后可维护性显著提升，模板残留清理干净
- **实施周期**：3-5 天（原方案 10 天）

---

**文档版本**：2.0  
**创建日期**：2026-06-21  
**最后更新**：2026-06-21  
**状态**：待审核
