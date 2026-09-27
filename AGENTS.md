# 仓库约定

- 默认使用中文回复；Git 提交信息优先使用中文。
- 本仓库是 Unity 桌面特效示例，新增效果应保持可单独触发和演示，避免引入产品规划、商业化或多进程守护架构。
- 真实桌面模式只能读取 Explorer 项目并渲染 Unity 代理。禁止自动隐藏、移动、删除或重命名真实桌面图标与文件。
- 演示/截图模式只能使用合成图标与程序生成的背景，不读取、截图或暴露用户的真实桌面。
- Native 桥接仅服务于 Windows 实际桌面模式；普通效果逻辑放在 `unity/InteractiveWallpaper3D/Assets/InteractiveWallpaper/Runtime/`。
- 修改效果后进行 Unity 编译；需要发布二进制时先构建 Release Native DLL，并从实际运行的 Player 截取演示图。
- 自有代码按 GPL-3.0-only 发布；第三方模型、视频和贴图应保留原有授权，其来源、许可证及修改记录写入 `THIRD_PARTY_NOTICES.md` 或素材目录。不要把 Unity Editor/Runtime 视为本仓库 GPL 授权内容。
