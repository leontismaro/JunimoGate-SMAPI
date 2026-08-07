[English](README.md) | **简体中文**

# JunimoGate SMAPI

JunimoGate SMAPI 是 [JunimoGate 启动器](https://github.com/leontismaro/JunimoGate)
使用的 Android SMAPI 分支。它作为启动器的 `smapi/` Git 子模块从源码构建，
并通过明确的 Android session contract 托管。

## Android 集成

该分支保留上游 SMAPI 行为，并加入 JunimoGate 所需的 Android 与 host 边界：

- `net9.0-android35.0` ARM64 构建目标；
- 由 host 提供的 Activity、存储、Content、存档、日志和 Mod 路径；
- Android 主线程调度、生命周期、View、输入与音频集成；
- 游戏、SMAPI 与 Mod 共用的 managed assembly 加载边界；
- Android 存档序列化器注册与可写用户数据分离；
- 结构化 session 启动以及向启动器返回结果；
- Android Mod 依赖绑定与可复用 rewrite cache。

维护中的 host contract 和进程模型见启动器仓库的
[SMAPI 架构](https://github.com/leontismaro/JunimoGate/blob/main/docs/smapi-architecture.md)。

## 来源与致谢

该分支的直接代码来源是：

- [Pathoschild/SMAPI](https://github.com/Pathoschild/SMAPI)：由 Pathoschild
  及贡献者维护的官方 SMAPI 项目；
- [NRTnarathip/SMAPI-Android-1.6](https://github.com/NRTnarathip/SMAPI-Android-1.6)：
  本分支采用的直接 Android runtime 来源。

感谢这两个项目以及此前相关社区工作的探索。确切提交、导入点与维护中的 patch
分层记录在[来源文档](docs/android/provenance.md)和
[Android patch 系列](docs/android/patch-series.md)中。

## 构建与验证

建议从递归克隆的 JunimoGate 启动器仓库进行构建，由主仓库提供固定版本的 Android
工具链以及本地构建的 Harmony 和 MonoGame 包。

Android 编译还需要一个目录，包含用作编译引用的游戏程序集：

```bash
export JUNIMOGATE_GAME_REFERENCE_DIR="/absolute/path/to/game/assemblies"
```

构建入口、所需输入和检查方式见 [Android 验证](docs/android/validation.md)，维护流程
见 [Android 分支升级指南](docs/android/upgrading.md)。

## 上游文档

保留的 SMAPI 文档以 [docs/README.md](docs/README.md) 为入口。SMAPI 玩家与 Mod
作者的通用文档见 [smapi.io](https://smapi.io/)。

## 许可证

JunimoGate SMAPI 采用
[GNU 宽通用公共许可证第 3 版](LICENSE.txt)，SPDX 标识为 LGPL-3.0-only。
修改后的 SMAPI 源码继续采用同一许可证。

第三方依赖分别保留各自许可证。JunimoGate 启动器代码位于独立仓库，并采用其自身
项目许可证。
