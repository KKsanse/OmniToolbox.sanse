# Sanse 的 Omni 在线模块仓库

本仓库发布 **园圃工作台 · Omni 版 1.0**，作者 **Sanse**。

## 安装

1. 安装或更新 Omni Toolbox，最低版本 **1.1.8.9**。
2. 在 Omni 的 **插件设置 → 在线模块** 中添加以下仓库地址：
https://github.com/yayakotiya-design/OmniToolbox.TreeHouseOnline
 

3. 刷新仓库，安装「园圃工作台」。
4. 在 **树树妙妙屋 → 在线** 中手动启用模块，首次安装默认关闭。
5. **需要开启自动跳过对话。**

如需直接填写清单地址：

```text
https://raw.githubusercontent.com/yayakotiya-design/OmniToolbox.TreeHouseOnline/main/TreeHouseModules.json
```

模块接口为 **API 1**。只需 Omni 提供的公共依赖，无需另装 DR、TreeTools、OmenTools 或 KamiToolKit。

## 功能

- 按格选择种子和土壤，保存及分享 4 / 6 / 8 格杂交布局。
- 管理多个园圃、布局绑定和园圃昵称。
- 种植、收获、护理与施肥操作。
- 作物识别、操作时间记录和到期提醒。
- GLW1 布局分享码导入与导出。

启用后可使用 `/omni KotiyaGarden open` 打开工作台。

## 从本地版切换

先停止任务、关闭园艺和选料界面，停用原本地模块，再安装在线版。不要同时启用两个相同模块。

内部模块标识仍为 `KotiyaGarden`，本次发布保持原 DLL 字节不变。模块自有布局与记录仍使用既有 `TreeHouseLocalData/KotiyaGarden/state.json` 路径，请保留原数据。宿主管理的模块设置是否在本地入口与在线入口间自动继承，需要实际确认；切换前可先备份配置。

## 文件

- `TreeHouseModules.json`：在线模块清单。
- `Modules/KotiyaGarden.dll`：安装文件。
- `source/`：完整分文件源码与项目文件。
- `KotiyaGarden.cs`：原包中的合并源码，供查阅或本地加载，不重复登记到在线清单。
- `LICENSE`、`NOTICE.md`：原许可及上游作者说明。

## 编译

使用 .NET 10 SDK，引用当前 Omni 和 Dalamud 的实际依赖。默认路径使用当前用户的 APPDATA；也可以显式指定目录（末尾保留 `/`）：

```powershell
dotnet build source/KotiyaGarden.csproj -c Release "-p:OmniSdk=D:/path/to/OmniToolbox/" "-p:DalSdk=D:/path/to/Dalamud/"
```

发布时仅上传模块 DLL，不打包宿主公共依赖。替换 DLL 后，递增清单中的 `Version`，重新计算 `Sha256`，并将模块与清单一起提交：

```powershell
(Get-FileHash ./Modules/KotiyaGarden.dll -Algorithm SHA256).Hash
```

## 验证状态

本仓库使用 `KotiyaGarden-Omni-1.0-Sanse.zip` 的已编译 DLL，已核对 DLL 和合并源码 SHA256 与原包记录一致。原包记录了源码编译及离线接口检查通过；本次仓库发布不代表已完成游戏内在线安装、种植、护理或施肥验证。

## 许可与参考

沿用原包 **AGPL-3.0-only** 许可及原作者声明，详见 [LICENSE](LICENSE) 和 [NOTICE.md](NOTICE.md)。当前模块作者署名为 Sanse，原上游贡献记录保留。

仓库格式参考 [Omni 在线模块接入文档](https://github.com/YouShux/OmniToolbox.TreeHouseOnline)。本仓库为个人模块仓库。
