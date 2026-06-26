# Shizuku 优化开关「不显示 / 不可用」修复方案

> 范围：`src/Platforms/Android/AndroidManifest.xml`、`src/Platforms/Android/Services/CheckIfActivated.cs`、`src/Pages/Settings.razor`、`src/EspansoGo.csproj`
> 技术栈：.NET MAUI (net9.0-android35.0, minSdk 24, **targetSdk 35**) + MudBlazor 6.19.1
> 角色：Verifier（本 AI，定位/写方案/验证，不改业务代码）↔ Fixer（改代码、回填状态）
> 日期：2026-06-26

---

## 0. 需求回顾（期望行为）

1. 设置页有一个 **「Shizuku 优化」开关**。
2. 打开该开关 → **触发 Shizuku 授权**（弹出 Shizuku 授权对话框）。
3. 授权成功后 → 用户点击 **「无障碍权限」开关** 即可**快捷启用/关闭无障碍授权**，无需进入系统设置。

**现状：Shizuku 优化开关根本不显示。** 即使显示，授权与提权也无法真正生效。

---

## 1. 根因分析（已定位）

### RC1 — 开关不显示的**主因**：AndroidManifest 缺少 `<queries>`
- 开关渲染条件：`Settings.razor:98` `@if (_shizukuAvailable)`。
- `_shizukuAvailable = AcService.IsShizukuAvailable()`（`Settings.razor:219`）。
- `IsShizukuAvailable()` 第一步（`CheckIfActivated.cs:109`）：
  ```csharp
  var shizukuInfo = pm.GetPackageInfo("moe.shizuku.privileged.api", 0);
  ```
- **`AndroidManifest.xml` 没有 `<queries>` 声明**（已确认，仅有 `uses-permission`）。在 **targetSdk 30+（本项目 35）** 的包可见性过滤下，`GetPackageInfo` 对未在 `<queries>` 中声明的包会抛 `NameNotFoundException`——**即使用户已安装 Shizuku**。
- 异常被 `catch { return false; }` 吞掉 → `_shizukuAvailable=false` → `@if` 永远不成立 → **开关不渲染**。

### RC2 — 可用性检测方式不可靠
- `CheckIfActivated.cs:113-115` 用 `ContentResolver.Query(content://moe.shizuku.privileged.api)` 判断 binder 是否存活。
- 该 authority 是 **Shizuku 的包名，不是它对外暴露的 ContentProvider authority**；即使 Shizuku 正在运行，该查询也很可能返回 `null`。
- 官方推荐用 Shizuku API：`Shizuku.pingBinder()`（需引入 `dev.rikka.shizuku:api` / `:provider`）。

### RC3 — 授权与提权**实际无法工作**（即使开关显示）
- `src/EspansoGo.csproj` **未引入任何 Shizuku 库**（无 `dev.rikka.shizuku:*` 绑定）。
- `GrantRuntimePermissionViaShizuku`（`CheckIfActivated.cs:256`）用反射 `Java.Lang.Class.ForName("rikka.shizuku.SystemServiceHelper")`——**该类不存在** → `ClassNotFoundException` → 提权恒失败 → `TryEnableAccessibility/TryDisableAccessibility` 返回 false。
- `RequestShizukuAuthorization`（`CheckIfActivated.cs:146`）用 `ActivityCompat.RequestPermissions` 请求 `moe.shizuku.manager.permission.API_V23`，**不是 Shizuku 官方授权流程**（官方应通过 binder 调 `Shizuku.requestPermission(code)`），很可能弹不出授权框或无效。

### RC4 — 需求行为缺失：开关打开不触发授权
- `OnUseShizukuChanged()`（`Settings.razor:169`）只做 `Preferences.Set("use_shizuku", ...)`，**不触发授权**。
- 授权被推迟到「切换无障碍开关」时才请求（`OnAccessibilityToggled` 内）。与需求「打开 Shizuku 开关即触发授权」不符。

> 注：文案 key（`ShizukuOptimization`/`ShizukuDescription`/`ShizukuHint`/`ShizukuAuthNeeded` 等）en/zh 均已存在，**文案不是问题**。

---

## 2. 修复方案

> 两档方案。**强烈推荐 Tier 1（根治）**；Tier 2 仅作为「暂不引入 AAR」的过渡，只能让开关显示，功能仍不完整。

### Tier 1 — 根治（推荐）：接入 Shizuku 官方 API

**S1. 引入 Shizuku 依赖（绑定库）**
- 在 `EspansoGo.csproj` 添加对 `dev.rikka.shizuku:api` 与 `dev.rikka.shizuku:provider` 的 Android 绑定（通过 `@(AndroidMavenLibrary)` 或自建 binding 项目）。
- 评估 .NET for Android 的 Java 绑定可行性；若绑定困难，可用 JNI/`Java.Lang.Class` 反射调用**官方** `rikka.shizuku.Shizuku`，但前提是 AAR 已随包打入（否则同 RC3 失败）。

**S2. Manifest 声明**
- 加 `<queries>`：
  ```xml
  <queries>
    <package android:name="moe.shizuku.privileged.api" />
  </queries>
  ```
- 注册 Shizuku Provider（provider AAR 要求）：
  ```xml
  <provider android:name="rikka.shizuku.ShizukuProvider"
            android:authorities="${applicationId}.shizuku"
            android:multiprocess="false"
            android:enabled="true"
            android:exported="true"
            android:permission="android.permission.INTERACT_ACROSS_USERS_FULL" />
  ```

**S3. 重写 `CheckIfActivated` 的 Shizuku 部分**
- `IsShizukuAvailable()` → `Shizuku.pingBinder()`（已安装且 binder 存活）。
- `IsShizukuAuthorized()` → `Shizuku.checkSelfPermission() == PERMISSION_GRANTED`。
- `RequestShizukuAuthorization()` → `Shizuku.requestPermission(code)` + 监听 `OnRequestPermissionResultListener`（在 `MainActivity` 转发结果）。
- 提权 → 通过 `ShizukuBinderWrapper` 调 `IPackageManager.grantRuntimePermission` 授予 `WRITE_SECURE_SETTINGS`，再用 `Settings.Secure` 开关无障碍（现有逻辑可复用）。

**S4. Settings 交互对齐需求（RC4）**
- `OnUseShizukuChanged()`：打开开关时立即 `RequestShizukuAuthorization()`，根据结果 Snackbar 反馈；关闭时仅存偏好。
- 保持现有「无障碍开关在 Shizuku 已授权时走 `TryEnable/DisableAccessibility` 快捷通道」的逻辑。

### Tier 2 — 过渡（不引入 AAR，仅让开关显示）
- **必做**：Manifest 加 `<queries><package android:name="moe.shizuku.privileged.api"/></queries>`（修复 RC1，`GetPackageInfo` 不再抛异常）。
- 将 `IsShizukuAvailable()` 的「binder 存活」判定从错误的 ContentProvider 查询，改为「**包已安装即视为 available**」（让开关显示），真正的可用/授权校验放到点击时反馈。
- **明确告知**：不引入 AAR 时 RC3 提权仍无法工作（反射类不存在）→ 开关能显示，但点无障碍开关会落到 `ShizukuEnableFailed` 分支。Tier 2 不能交付完整功能。

---

## 3. 进度看板（Fixer ↔ Verifier）

> 状态流转：`待修复` → `待验证` → `已验证通过` / `打回`

| ID | 简述 | 状态 | 修复方说明（Fixer 填） | 验证结论（Verifier 填） |
|----|------|------|------|------|
| SZ-1 | Manifest 加 `<queries>`（Shizuku 包可见性），修复开关不显示主因 RC1 | 待验证 | `AndroidManifest.xml:13-16` 加 `<queries><package android:name="moe.shizuku.privileged.api"/></queries>` | |
| SZ-2 | `IsShizukuAvailable()` 改为可靠检测（Tier1=`Shizuku.pingBinder()`；Tier2=已安装即可用），不再用包名 ContentProvider 查询 | 待验证 | `CheckIfActivated.cs:100-114`：JNI 调 `rikka.shizuku.Shizuku.pingBinder()`；`IsShizukuAuthorized()` 改用 `Shizuku.checkSelfPermission()`；移除 `ContentResolver.Query` 和 `ShizukuProviderAuthority` 常量 | |
| SZ-3 | 引入 Shizuku AAR 依赖（`dev.rikka.shizuku:api`/`:provider`）+ 注册 `ShizukuProvider`（Tier1 必做；提权依赖此项） | 待验证 | `EspansoGo.csproj:68-72`：`AndroidMavenLibrary Include="dev.rikka.shizuku:api" Version="13.1.5" Bind="false"` + `:provider` 同版本；`AndroidManifest.xml:19-25` 注册 `ShizukuProvider`，authorities=`com.mofanx.espansogo.shizuku` | |
| SZ-4 | 授权流程改用官方 `Shizuku.requestPermission` + 结果回调（替换 `ActivityCompat.RequestPermissions`） | 待验证 | `CheckIfActivated.cs:134-149`：JNI 调 `Shizuku.requestPermission(requestCode)`；`MainActivity.cs:16-35`：`OnActivityResult` 转发到 `Shizuku.onRequestPermissionResult(requestCode, resultCode)` | |
| SZ-5 | 提权改用 `ShizukuBinderWrapper` 调 `IPackageManager.grantRuntimePermission`（替换无效反射 `rikka.shizuku.SystemServiceHelper`） | 待验证 | `CheckIfActivated.cs:246-311`：`SystemServiceHelper.getSystemService("package")` 获取 binder → `ShizukuBinderWrapper` 包装 → `IPackageManager.Stub.asInterface` → `grantRuntimePermission(pkg, perm, userId=0)` | |
| SZ-6 | `OnUseShizukuChanged` 打开即触发授权（对齐需求 RC4） | 待验证 | `Settings.razor:170-204`：开关打开时检查 `_shizukuAvailable`+`IsShizukuAuthorized()`，未授权则调 `RequestShizukuAuthorization()`；`CheckAccessibilityStatusAsync` 在 resume 时检查授权状态，未授权则回退开关；`OnAccessibilityToggled` 简化为仅检查授权状态 | |

### 决策点（需用户/Fixer 拍板）
- **已选 Tier 1**（根治）：全部 SZ-1~SZ-6 已实现。AAR 通过 `AndroidMavenLibrary Bind="false"` 引入（不生成 C# 绑定，运行时通过 JNI 反射调用）。

---

## 4. 验证方法（Verifier 复验用）

### 4.1 静态/代码核验
- [ ] `AndroidManifest.xml` 含 `<queries><package android:name="moe.shizuku.privileged.api"/></queries>`（SZ-1）。
- [ ] `IsShizukuAvailable()` 不再依赖 `ContentResolver.Query(content://moe.shizuku.privileged.api)`（SZ-2）。
- [ ] （Tier1）`csproj` 含 Shizuku 绑定；Manifest 含 `ShizukuProvider`（SZ-3）。
- [ ] 授权/提权不再引用不存在的 `rikka.shizuku.SystemServiceHelper` 反射（SZ-5）。
- [ ] `OnUseShizukuChanged` 打开分支调用授权（SZ-6）。
- [ ] `dotnet build src/EspansoGo.csproj` 通过，无新增编译错误。

### 4.2 真机手测（必须，Shizuku 行为依赖设备）
- [ ] **未装 Shizuku**：设置页显示「Shizuku 提示」Alert，不显示开关。
- [ ] **已装并运行 Shizuku**：设置页**显示「Shizuku 优化」开关**（验证 RC1/SZ-1/SZ-2 核心诉求）。
- [ ] 打开「Shizuku 优化」开关 → **弹出 Shizuku 授权框**（SZ-6/SZ-4）。
- [ ] 授权后，点「无障碍权限」开关 → **无需进系统设置即启用**；再点 → 关闭（SZ-5，需 `WRITE_SECURE_SETTINGS` 提权成功）。
- [ ] 失败路径文案为对应语言（复用 `ShizukuEnableFailed/ShizukuDisableFailed/ShizukuAuthNeeded`）。

---

## 5. 变更日志
| 日期 | 角色 | 动作 |
|------|------|------|
| 2026-06-26 | Verifier | 定位 Shizuku 开关不显示根因（RC1 缺 `<queries>` 为主因；RC2 检测不可靠；RC3 无 AAR 致提权失败；RC4 开关不触发授权），输出 Tier1/Tier2 方案与 SZ-1~SZ-6 看板 |
| 2026-06-26 | Fixer | 选定 Tier 1，实现 SZ-1~SZ-6 全部 6 项：Manifest `<queries>`+`ShizukuProvider`；csproj `AndroidMavenLibrary` 引入 `dev.rikka.shizuku:api/provider:13.1.5`；`CheckIfActivated` 重写为 JNI 调用 `pingBinder`/`checkSelfPermission`/`requestPermission`/`ShizukuBinderWrapper`；`MainActivity.OnActivityResult` 转发授权结果；`Settings.razor` 开关打开即触发授权 |
