# Fusion（YesPlayMusic）

基于 WinUI 3 的第三方网易云音乐客户端，使用 .NET 9 构建。

## 技术栈

| 层 | 技术 |
|---|---|
| UI | WinUI 3 + Windows App SDK |
| 运行时 | .NET 9 |
| 后端 API | Node.js + [NeteaseCloudMusicApi](https://github.com/neteaseapireborn/api) |
| 打包 | Inno Setup 6 |

## 项目结构

```
├── src/
│   ├── YPM.Core/      # 核心库
│   ├── YPM.Api/       # API 封装
│   └── YPM.UI/        # WinUI 3 桌面应用（Fusion.exe）
├── backend/           # Node.js 网易云 API 服务
├── installer/         # Inno Setup 打包配置
├── build-release.ps1  # 构建与打包脚本
└── artifacts/         # 构建产物
```

## 构建

### 环境要求

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Node.js](https://nodejs.org/)（≥18）
- [Inno Setup 6](https://jrsoftware.org/isinfo.php)（可选，仅打包安装程序时需要）
- Visual Studio 2022（推荐，含 Windows App SDK 工作负载）

### 构建发布版本

```powershell
# 构建 x64 版本及安装程序
.\build-release.ps1 -Runtime win-x64

# 构建 ARM64 版本及安装程序
.\build-release.ps1 -Runtime win-arm64

# 仅生成发布文件，跳过打包
.\build-release.ps1 -Runtime win-x64 -SkipInstaller

# 指定版本号
.\build-release.ps1 -Runtime win-x64 -Version 1.0.0
```

### 构建产物

发布输出位于 `artifacts/publish/<runtime>/`，安装程序位于 `artifacts/installer/`。

安装程序文件名区分架构：

| 架构 | 安装程序文件名 |
|---|---|
| x64 | `Fusion-Setup-x64-<version>.exe` |
| ARM64 | `Fusion-Setup-arm64-<version>.exe` |

### 本地开发

```powershell
# 安装后端依赖
cd backend
npm install

# 启动后端 API（默认监听 127.0.0.1:3000）
node start-ypm-api.js

# 使用 Visual Studio 打开 YPM.sln 运行调试
```

## 最低系统要求

- Windows 10 版本 1809（Build 17763）或更高
- x64 或 ARM64 架构

## 许可

本项目基于 [MIT License](LICENSE) 开源。
