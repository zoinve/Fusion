# YesPlayMusic → WinUI 3 移植任务清单

> **目标**: 将 YesPlayMusic (Vue 2 + Electron) 的前端移植为 WinUI 3 原生应用，前端结构复用，后端 API 完全复用。

---

## Phase 0: 项目初始化

- [x] **0.1** 创建 WinUI 3 项目 (Windows App SDK, .NET 9, C#)
- [x] **0.2** 配置解决方案结构 (src/YPM.Core, src/YPM.UI, src/YPM.Api)
- [x] **0.3** 安装 NuGet 依赖: CommunityToolkit.Mvvm, CommunityToolkit.WinUI, Microsoft.WindowsAppSDK
- [x] **0.4** 安装辅助依赖: Newtonsoft.Json / System.Text.Json, Sqlite-net-pcl, H.NotifyIcon
- [x] **0.5** 配置 .editorconfig, .gitignore, Directory.Build.props
- [x] **0.6** 建立项目目录结构 (Pages, Controls, ViewModels, Services, Models, Helpers, Assets)

## Phase 1: 核心基础设施

- [ ] **1.1** API 服务层 — HttpClient 封装, Base URL 配置, Cookie 管理, 请求/响应拦截器 (对应 `src/utils/request.js`)
- [ ] **1.2** API 模块 — 逐模块实现: auth, user, playlist, track, album, artist, mv, search, lastfm (对应 `src/api/*.js`)
- [ ] **1.3** 音频播放服务 — MediaPlayer 封装, 播放/暂停/切歌/进度控制/音量, 播放队列管理 (对应 `src/utils/Player.js`)
- [ ] **1.4** 本地缓存服务 — SQLite 数据库, 缓存音轨详情/歌词/专辑/音频源, 缓存上限管理 (对应 `src/utils/db.js`)
- [ ] **1.5** 导航服务 — Frame 导航封装, 路由定义, 导航守卫(登录检查), 页面缓存策略 (对应 `src/router/index.js`)
- [ ] **1.6** 本地化服务 — 资源文件 (.resw), 语言切换, 中/英/繁/土耳其语 (对应 `src/locale/`)
- [ ] **1.7** 主题/样式系统 — WinUI 主题, 自定义样式资源字典, 颜色/字体/间距 (对应 `src/assets/css/`)

## Phase 2: 状态管理 (MVVM)

- [ ] **2.1** 应用设置服务 — 语言/外观/音质/快捷键/代理等设置的读写与持久化 (对应 Vuex `settings`)
- [ ] **2.2** 用户认证服务 — 登录状态, Cookie 管理, Token 验证, 自动登出 (对应 Vuex `data` + `src/utils/auth.js`)
- [ ] **2.3** 播放器状态管理 — 当前音轨, 播放进度, 播放模式, 音量, 队列 (对应 Vuex `player`)
- [ ] **2.4** 用户内容状态 — 我喜欢/收藏的歌单/专辑/艺人/MV, 云盘, 播放历史 (对应 Vuex `liked`)
- [ ] **2.5** UI 状态 — Toast 通知, 模态框, 右键菜单, 歌词显示 (对应 Vuex `toast`, `modals`, `contextMenu`)

## Phase 3: UI 外壳与导航

- [ ] **3.1** 主窗口 — Window 配置, 标题栏, 最小化/最大化/关闭, 窗口位置记忆
- [ ] **3.2** NavigationView 外壳 — 左侧导航菜单, 页面 Frame, 响应式布局 (对应 `App.vue` + `Navbar.vue`)
- [ ] **3.3** 底部播放栏 — 封面, 音轨信息, 进度条, 播放控制, 音量, 喜欢按钮, 队列按钮 (对应 `Player.vue`)
- [ ] **3.4** 系统托盘 — 托盘图标, 右键菜单, 播放控制 (对应 `electron/tray.js`)

## Phase 4: 页面实现

- [ ] **4.1** 登录页 — 二维码登录, 二维码刷新, 登录状态轮询 (对应 `login.vue`)
- [ ] **4.2** 手机/邮箱登录页 (对应 `loginAccount.vue`)
- [ ] **4.3** 用户名登录页 (对应 `loginUsername.vue`)
- [ ] **4.4** 首页 — Banner 轮播, 推荐歌单, 推荐歌曲, 新碟速递, 排行榜 (对应 `home.vue`)
- [ ] **4.5** 歌单详情页 — 歌单信息头, 曲目列表, 播放/收藏/评论 (对应 `playlist.vue`)
- [ ] **4.6** 专辑详情页 — 专辑信息头, 曲目列表, 艺人信息 (对应 `album.vue`)
- [ ] **4.7** 艺人详情页 — 艺人信息, 热门歌曲, 专辑列表, MV 列表 (对应 `artist.vue`)
- [ ] **4.8** 艺人 MV 页 (对应 `artistMV.vue`)
- [ ] **4.9** MV 播放页 — 视频播放器, MV 信息, 相关推荐 (对应 `mv.vue`)
- [ ] **4.10** 搜索页 — 搜索框, 综合搜索结果, 分类 Tab (对应 `search.vue`)
- [ ] **4.11** 分类搜索结果页 (对应 `searchType.vue`)
- [ ] **4.12** 音乐库页 — 我喜欢/收藏/云盘/播放历史, Tab 切换 (对应 `library.vue`)
- [ ] **4.13** 设置页 — 所有设置项, 快捷键配置, 代理设置 (对应 `settings.vue`)
- [ ] **4.14** 每日推荐页 (对应 `dailyTracks.vue`)
- [ ] **4.15** 发现/探索页 (对应 `explore.vue`)
- [ ] **4.16** 新碟上架页 (对应 `newAlbum.vue`)
- [ ] **4.17** 全屏歌词视图 (对应 `lyrics.vue`)
- [ ] **4.18** 播放队列页 (对应 `next.vue`)
- [ ] **4.19** Last.fm 回调页 (对应 `lastfmCallback.vue`)

## Phase 5: 可复用控件

- [ ] **5.1** CoverControl — 专辑/歌单封面, 懒加载, 占位图, 圆角/阴影 (对应 `Cover.vue`)
- [ ] **5.2** CoverRowControl — 封面横向列表 (对应 `CoverRow.vue`)
- [ ] **5.3** TrackListControl — 曲目列表, 列头, 排序, 虚拟化滚动 (对应 `TrackList.vue`)
- [ ] **5.4** TrackListItemControl — 曲目行, 序号/封面/标题/艺人/专辑/时长, 双击播放 (对应 `TrackListItem.vue`)
- [ ] **5.5** ArtistsInLineControl — 艺人名逗号分隔链接 (对应 `ArtistsInLine.vue`)
- [ ] **5.6** ContextMenu — 右键菜单 (对应 `ContextMenu.vue`)
- [ ] **5.7** Modal / ContentDialog — 通用模态框 (对应 `Modal.vue`)
- [ ] **5.8** ModalAddTrackToPlaylist — 添加到歌单弹窗 (对应 `ModalAddTrackToPlaylist.vue`)
- [ ] **5.9** ModalNewPlaylist — 新建歌单弹窗 (对应 `ModalNewPlaylist.vue`)
- [ ] **5.10** ToastControl — 通知提示 (对应 `Toast.vue`)
- [ ] **5.11** SvgIconControl — SVG 图标支持 (对应 `SvgIcon.vue`)
- [ ] **5.12** ButtonIconControl — 图标按钮 + 提示 (对应 `ButtonIcon.vue`)
- [ ] **5.13** ButtonTwoToneControl — 双色按钮 (对应 `ButtonTwoTone.vue`)
- [ ] **5.14** ExplicitSymbolControl — Explicit 标记 (对应 `ExplicitSymbol.vue`)
- [ ] **5.15** FMCardControl — 私人 FM 卡片 (对应 `FMCard.vue`)
- [ ] **5.16** DailyTracksCardControl — 每日推荐卡片 (对应 `DailyTracksCard.vue`)
- [ ] **5.17** MvRowControl — MV 展示行 (对应 `MvRow.vue`)

## Phase 6: 功能整合

- [ ] **6.1** 登录流程 — 二维码/手机/邮箱/用户名多种登录方式, 登录状态同步
- [ ] **6.2** 音乐播放 — 播放 URL 获取, 音质切换, 播放模式(列表/单曲/随机), 自动续播
- [ ] **6.3** 搜索功能 — 关键词搜索, 搜索建议, 分类搜索, 搜索历史
- [ ] **6.4** 喜欢/收藏 — 我喜欢歌曲, 收藏歌单/专辑/MV, 状态同步
- [ ] **6.5** 歌单管理 — 创建/删除/编辑歌单, 添加/移除歌曲
- [ ] **6.6** 每日推荐 — 推荐歌曲/歌单获取与展示
- [ ] **6.7** 私人 FM — FM 播放, 垃圾桶(跳过), 喜欢
- [ ] **6.8** Last.fm 集成 — OAuth 登录, 正在播放/Scrobble
- [ ] **6.9** 系统媒体传输控件 (SMTC) — 媒体键集成, 锁屏显示
- [ ] **6.10** 全局快捷键 — 播放/暂停, 上一首/下一首, 音量
- [ ] **6.11** 通知推送 — 切歌通知, 登录状态变更

## Phase 7: 抛光与发布

- [ ] **7.1** 性能优化 — 虚拟化列表, 图片缓存, 后台任务, 内存管理

- [ ] **7.2** 错误处理 — 网络异常, API 错误, 播放失败, 全局异常捕获

- [ ] **7.3** 动画与过渡 — 页面切换动画, 播放栏动画, 悬停效果

- [ ] **7.4** 响应式布局 — 窗口缩放适配, 最小窗口尺寸

  

---

## 技术映射速查

| Vue.js (原项目) | WinUI 3 (目标) |
|---|---|
| Vue 2.6 SFC (.vue) | XAML Page / UserControl + C# code-behind |
| Vuex Store | CommunityToolkit.Mvvm (ObservableObject, ObservableProperty) |
| Vue Router | Frame.Navigate + NavigationView |
| Axios + request.js | HttpClient + ApiService |
| Howler.js | Windows.Media.Playback.MediaPlayer |
| Dexie (IndexedDB) | SQLite (sqlite-net-pcl) |
| Vue I18n | Windows.ApplicationModel.Resources + .resw |
| SCSS | XAML ResourceDictionary + Theme Resources |
| Electron IPC | 不需要 (原生 Windows API) |
| electron-store | Windows.Storage.ApplicationData |
| Electron tray | H.NotifyIcon / Windows App SDK AppWindow |
| vue-gtag | 可移除 (或 Google Analytics Measurement Protocol) |
| node-vibrant | Microsoft.UI.Xaml.Media 或 SkiaSharp |
| QRCode.js | QRCoder (NuGet) |
| crypto-js | System.Security.Cryptography |
| dayjs | System.DateTime / DateTimeOffset |
| NProgress | ProgressBar / ProgressRing |
