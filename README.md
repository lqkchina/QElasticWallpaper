# Q弹桌面壁纸（QElasticWallpaper）

一个 Win10/Win11 桌面小程序：**在桌面上点击鼠标左键，壁纸上会立刻出现「真人皮肤按压 + Q弹回弹 + 波纹扩散」的果冻般反馈效果**。

- 效果画在独立的透明层上，**与壁纸内容无关**——你随便换壁纸，效果照常工作。
- 效果层默认垫在**桌面图标之下、壁纸之上**，不挡图标、不挡任何窗口。
- **所有参数都能在设置窗口里手动调节**，改了即时生效、自动保存。
- 软件自带一套**「默认最优」预设**，装好即用，不用调任何东西。

---

## 效果预览

| 交互 | 效果 |
|---|---|
| 桌面空白处 左键点击 | 皮肤凹陷 + Q弹回弹 + 多层波纹扩散 |
| 快速连续点击 | 多个效果叠加，越点越热闹 |
| （可选）鼠标悬停桌面 | 一圈淡淡光晕跟随 |

默认只有**点击壁纸空白处**才触发，不影响你点桌面图标和应用窗口。

---

## 怎么用（GitHub 打包）

你有两件事：**传源码** 和 **出 EXE**。源码直接整个文件夹丢进 GitHub 仓库即可；EXE 在你自己 Windows 电脑上跑一条命令生成。

### 1. 环境要求
- Windows 10/11 电脑
- 安装 [.NET 7 SDK](https://dotnet.microsoft.com/download/dotnet/7.0)（`dotnet --version` 确认）

### 2. 在 Windows 上构建/运行

```bat
cd src\QElasticWallpaper

:: 直接运行（调试）
dotnet run

:: 构建普通版本
dotnet build -c Release

:: 生成单文件 EXE（推荐发 GitHub Release 用）
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

发布后得到 `publish\QElasticWallpaper.exe`，**双击即用，无需装 .NET**（已自包含运行时）。
小文件版（依赖系统 .NET，体积小很多）：

```bat
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

> 如果想再压缩体积，可用 `ILRepack` 或先 `PublishTrimmed`，本工程默认未开启裁剪以保证稳定。

### 3. 传 GitHub

```bat
git init
git add .
git commit -m "Q弹桌面壁纸 v1.0"
git remote add origin https://github.com/<你的用户名>/QElasticWallpaper.git
git push -u origin master
```

在 GitHub 的 **Releases** 页面新建 Release，把 `publish\QElasticWallpaper.exe` 上传即可让别人下载。

### 4. 自动打包 EXE（推荐，不用自己开电脑编译）

项目里已带 `.github/workflows/build-exe.yml`（GitHub Actions 工作流）。只要包含 `.github` 文件夹一起推上去，**以后上传源码就自动出 EXE**，你再也不用在 Windows 上手动 `dotnet build`：

- **每次 push** → 后台自动在 `windows-latest` 上编译，出 `QElasticWallpaper.exe`，可在 Actions 页面的「Artifacts」里下载。
- **打 tag**（`git tag v1.0.0 && git push --tags`）→ 自动生成 GitHub **Release**，并把 EXE 挂上去，别人可直接下载。

> 注意：`git add .` 时务必把 `.github/` 目录一起提交（`.gitignore` 不会忽略它）。仓库首次启用 Actions 通常立即生效，无需额外设置。

如果不想要自动构建，也可以忽略该工作流，纯按第 3 步手动出 EXE。

### 5. 版本号与回滚

- 版本号定义在 `src/QElasticWallpaper/QElasticWallpaper.csproj` 的 `<Version>1.0.0</Version>`。
- **每次修复 bug 后**，把这里的版本号 +1（如 `1.0.0 → 1.1.0`），打出的 EXE 文件名会自动带上版本号（`QElasticWallpaper-1.1.0.exe`），便于区分和回滚。
- 打 tag（`git tag v1.1.0 && git push --tags`）会在 GitHub 生成对应版本的 **Release**，旧版本永远保留，随时可回滚下载。
- 修改版本号后记得同时更新 `README.md` 顶部或版本说明，保持一致。

---

## 全部可调参数

设置窗口（托盘图标 →「打开设置…」，或双击托盘图标）可调以下所有参数：

| 参数 | 说明 |
|---|---|
| 启用效果 | 总开关 |
| 触发范围 | 仅桌面壁纸 / 桌面+应用背景 / 任意左键点击 |
| 波纹层数 | 一次点击扩散几圈波纹（1–16） |
| 按压基准半径 | 皮肤凹陷的初始大小（px） |
| 波纹扩散距离 | 波纹向外跑的行程（px） |
| 波纹粗细 | 每条波纹线宽（px） |
| 整体强度 | 效果最大透明度/浓度 |
| Q弹回弹次数 | 回弹振荡次数，越大越果冻感（0=只压不回弹） |
| 回弹衰减 | 振荡衰减，越小越软越久 |
| 单次动画时长 | 一次按压的总时长（ms） |
| 按压凹陷深度 | 中心暗色阴影深浅，模拟按进去 |
| 真人皮肤感 | 0=纯色扁平，1=皮肤般的柔光渐变与高光 |
| 高光强度 | 按压边缘拉伸皮肤的亮圈 |
| 边缘柔化 | 波纹边缘羽化程度 |
| 整体透明度 | 整个效果层透明度 |
| 随机波动 | 每次点击加一点随机差异，避免千篇一律 |
| 同屏效果上限 | 快速连点时的效果数量上限，防卡顿 |
| 渲染帧率 | 动画帧率（fps） |
| 悬停光晕 | 开关 + 半径 + 强度 |
| 按压音效 | 开关 + 音量 |
| 效果层位置 | 桌面图标之下 / 置顶显示 |
| 开机自启 | 登录时自动运行 |
| 启动即最小化 | 启动后只驻留托盘 |

**预设方案：** 默认最优（推荐）/ 极致Q弹果冻 / 柔和淡雅 / 鲜艳活力 / 极简克制。

**配置文件** 保存在 `%AppData%\QElasticWallpaper\config.json`，删掉它即恢复出厂。

---

## 技术说明（想自己改特效的人看）

- 框架：`.NET 7` + **WPF**（透明窗口 + `DrawingVisual` 逐帧绘制），纯代码、无 XAML，方便改。
- 点击捕获：`WH_MOUSE_LL` 全局低级鼠标钩子，独立线程跑消息循环，不卡 UI。
- 桌面判定：`WindowFromPoint` + `GetAncestor(GA_ROOT)` 判断点击是否落在桌面壁纸层。
- 效果层定位：全虚拟桌面透明窗口 + `SetWindowPos(HWND_BOTTOM)`，垫在图标和窗口下面。
- Q弹视觉：按压的皮肤凹陷用「径向渐变 + 阻尼正弦振荡」实现，波纹环同理带衰减振荡。

### 目录结构
```
QElasticWallpaper/
├─ src/QElasticWallpaper/
│  ├─ QElasticWallpaper.csproj
│  ├─ app.manifest            # DPI 感知、免管理员
│  ├─ Program.cs              # 入口 + 单实例
│  └─ Core/
│     ├─ AppConfig.cs         # 全部参数 + 预设（加参数就改这里）
│     ├─ ConfigStore.cs       # JSON 配置读写
│     ├─ Native.cs            # Win32 P/Invoke
│     ├─ MouseHook.cs         # 全局鼠标钩子
│     ├─ RippleController.cs  # 效果渲染数学（按压/回弹/波纹）
│     ├─ RippleEffect.cs      # 单次点击效果状态
│     ├─ OverlayWindow.cs     # 透明效果层窗口
│     ├─ SettingsWindow.cs    # 自动生成的参数面板
│     ├─ TrayHost.cs          # 系统托盘
│     └─ AppController.cs     # 总控制器
├─ build-publish.bat          # 一键出 EXE
├─ .gitignore
└─ LICENSE
```

---

## 常见问题

**问：效果没出现？**
1. 确认托盘图标在、且「启用效果」是勾选的；
2. 确认点击的是**壁纸空白处**（不要点在图标或开始菜单上）；
3. 若用了第三方壁纸软件（如 Wallpaper Engine），效果层可能被它盖住，把「效果层位置」切成「置顶显示」试试。

**问：会不会挡我操作？**
默认垫在图标和窗口下面，完全不吃鼠标（`IsHitTestVisible=false`），不会挡任何点击。

**问：换壁纸会失效吗？**
不会。效果画在独立透明层上，跟壁纸无关。

---

## License
[MIT](LICENSE)
