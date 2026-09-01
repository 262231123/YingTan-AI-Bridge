# 盈碳 AI Bridge 产品线

产品统一采用“盈碳 + 宿主软件 + AI Bridge”命名，并共享本地桥接、模型 API 配置、Agent、Skill、Dry-run 与用户确认的安全原则。

Revit、AutoCAD 和 Rhino 均可在各自插件内编辑同一份共享配置。API Key 使用当前 Windows 用户的 DPAPI 加密；保存前自动生成备份，任一宿主保存后其他宿主会在下一次调用时读取最新设置。

| 产品 | 当前交付状态 | 本机验证条件 |
| --- | --- | --- |
| 盈碳 Revit AI Bridge | 已有停靠式对话助手、模型配置、Agent/Skill 和受控执行 | Revit 2027 已安装 |
| 盈碳 Rhino AI Bridge | Rhino 8 对话窗、共享 API/Agent/Skill、模型上下文与受控建模命令 | Rhino 8 已安装并注册插件 |
| 盈碳 AutoCAD AI Bridge | AutoCAD 2022 右侧 Palette、共享配置、DWG 上下文与受控绘图命令 | AutoCAD 2022 已安装并部署 bundle |
| 盈碳 Autodesk Inventor AI Bridge | 公共协议与 Inventor AddIn 条件编译入口 | 需安装 Inventor 与互操作程序集 |

## 统一操作约定

1. Agent 或 Skill 只能生成结构化操作计划，不能绕过宿主适配器直接修改模型。
2. 查询和统计操作可直接运行，但必须由宿主 API 取数，再由 AI 解释结果。
3. 创建、修改、删除和原生命令操作必须先 Dry-run，再显示影响范围，并要求用户明确确认。
4. 每个宿主维护自己的对象映射：Revit 是元素与事务，AutoCAD 是数据库与命令上下文，Rhino 是文档与对象表，Inventor 是文档、组件和特征。

## 下一阶段

在 AutoCAD 2022 与 Rhino 8 中完成真实界面加载和模型操作回归测试；随后将共享服务接入 Inventor，补充跨宿主会话管理、安装包签名和版本升级机制。
