# osu_patterncopier v1.0.1

osu!stable 编辑器跨谱面对象复制工具。

程序常驻 Windows 托盘，自动识别源谱面、目标谱面和目标时间，通过备份后修改目标 `.osu` 文件实现跨谱复制。支持单个滑条，也支持一次复制一整组对象。

## 功能

- 自动识别当前 osu!stable 编辑器及谱面文件；
- 跨不同难度、不同谱面复制对象；
- 保留滑条控制点、重复次数、采样和其他对象字段；
- 整组复制时保持对象之间的相对时间；
- 根据目标编辑器当前时间平移整组对象；
- 写入前创建 `.slidercopier.bak` 备份；
- 不注入键盘、不写 osu! 进程内存、不需要管理员权限。

## 安装与运行

### 普通用户（推荐）

1. 打开本项目 GitHub 页面的 [Releases](https://github.com/Ribet-Code/osu_patterncopier/releases)。
2. 下载最新版本中的 `osu_patterncopier-v1.0.1-win-x64.zip`（或页面显示的最新文件名）。
3. 将 ZIP 文件解压到一个固定目录，例如 `C:\Tools\osu_patterncopier`。
4. 双击解压目录中的 `osu_patterncopier.exe` 启动程序。程序会缩小到 Windows 托盘，不需要安装器，也不需要管理员权限。
5. 如果 Windows Defender SmartScreen 显示提示，请先确认文件来自本项目的 GitHub Release，再选择“更多信息”→“仍要运行”。

发布包是 Windows x64 自包含版本，已包含 .NET 运行时和 `EditorReader.dll`，不需要另行安装 .NET。请保持压缩包内的所有文件在同一个目录中，不要只单独复制 `.exe`。

### 卸载

退出托盘程序后，直接删除解压目录即可。工具写入谱面时产生的 `.osu.slidercopier.bak` 备份文件不会自动删除。

## 操作说明

1. 运行解压目录中的 `osu_patterncopier.exe`。
2. 在 osu!stable 源谱面中选中一个滑条，或框选一整组对象，按 `Ctrl+C`。
3. 切换到目标谱面，把时间线定位到对象组的起始位置。
4. 在 osu! 编辑器内手动按 `Ctrl+S` 保存目标谱面。
5. 按 `Ctrl+V`。工具会自动读取源对象、目标谱面和目标时间，并将对象写入目标 `.osu`。
6. 等待托盘提示“滑条已写入目标文件”。
7. 在 osu! 编辑器内手动按 `Ctrl+L`（Revert to Saved）重新载入，使对象显示出来。

### 重要顺序

必须先保存目标谱面，再按 `Ctrl+V`。如果目标编辑器存在未保存修改，工具写入文件后，编辑器仍可能用内存中的旧状态覆盖文件。

## 备份与恢复

第一次写入某个目标文件时，工具会创建：

```text
目标文件.osu.slidercopier.bak
```

托盘菜单中的“恢复目标谱面备份”会将目标文件恢复到首次写入前的状态。恢复后仍需在 osu! 中手动按 `Ctrl+L`。

## 托盘菜单

- **选择源谱面**：EditorReader 无法自动解析当前版本时的备用选项；
- **选择目标谱面**：手动指定写入文件；
- **恢复目标谱面备份**：恢复首次写入前的备份；
- **退出**：退出后台程序。

## 工作原理与限制

- 工具读取 osu!stable 剪贴板中的时间戳，并使用 EditorReader 读取编辑器内部 clipboard 对象；
- 源对象从当前 osu! 进程中读取，目标谱面和目标时间也从当前编辑器读取；
- 对象组按最早对象时间对齐到目标时间；
- 工具只修改目标 `.osu` 文件，不修改源谱面；
- 工具不会自动保存、刷新或控制 osu! 窗口，必须由用户手动完成 `Ctrl+S` 和 `Ctrl+L`；
- EditorReader 依赖 osu!stable 的内部内存布局，osu! 更新后可能需要适配；
- 当前主要面向 osu!standard 的 `.osu` 对象格式。

## 从源码构建

要求：

- Windows 10/11；
- .NET 8 SDK；
- osu!stable。

```powershell
dotnet restore .\osu_patterncopier.csproj
dotnet build .\osu_patterncopier.csproj -c Release
dotnet publish .\osu_patterncopier.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o .\publish
```

发布目录中的 `osu_patterncopier.exe` 可直接运行。发布时请将整个 `publish` 目录打包，不要只上传 `.exe`。

## 第三方组件与开源许可

本项目源码以 MIT License 发布。`EditorReader.dll` 来自 [Karoo13/EditorReader](https://github.com/Karoo13/EditorReader)，其许可文本见 [`external/EditorReader/LICENSE`](external/EditorReader/LICENSE)。

本项目为社区工具，与 osu!、ppy 或 osu!stable 官方没有隶属关系。
