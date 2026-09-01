# 盈碳 Skill Hub 设计

## 结论

现有生态已经有成熟的宿主插件分发渠道：Autodesk Design and Make Marketplace 可筛选 Revit 应用；Rhino 8 的 PackageManager 可发现、安装和更新 Rhino/Grasshopper 插件；pyRevit 也能从第三方扩展目录发现并启用 UI 与库扩展。通用 AI Agent 领域则已有以 Git/GitHub 为分发基础的 Skills 市场和索引。

这些渠道解决的是“下载插件”或“下载提示词”，没有同时解决工程自动化的四件事：当前宿主/版本是否兼容、Skill 需要哪些受控能力、发布者是否可信、模型修改是否可审计。因此盈碳不应复制传统 App Store，而应提供宿主感知、受控执行的 Skill Hub。

参考：

- [Autodesk Marketplace - Revit](https://marketplace.autodesk.com/search?productIds=RVT)
- [Rhino 8 PackageManager](https://docs.mcneel.com/rhino/8/help/en-us/commands/packagemanager.htm)
- [Rhino Yak Package Manager Guides](https://developer.rhino3d.com/guides/yak/)
- [pyRevit Extension Manager](https://docs.pyrevitlabs.io/reference/pyrevit/extensions/extensionmgr/)

## 产品定位

产品名称：**盈碳 Skill Hub**。它是盈碳 Revit/AutoCAD/Rhino/Inventor AI Bridge 的能力目录与治理服务，不是另一个可随意运行 DLL、脚本或 Dynamo 文件的市场。

用户在插件的“Skills”页看到三个视图：

1. **已安装**：启用/停用、版本、来源、信任等级、更新与回滚。
2. **发现**：按宿主、软件版本、专业、项目阶段、写入风险筛选，并一键安装兼容 Skill。
3. **项目私库**：企业可从 Git、内网或离线包同步经过审批的团队 Skill。

每个 Skill 卡片显示适用宿主、最低 Bridge 版本、触发词、推荐命令、是否涉及写入、测试通过的宿主版本、发布者、更新日志、评分与问题反馈。模型只加载本轮相关的 Skill，不将整个仓库提示词都发送给大模型。

## 安全边界

第一期 Skill 是**声明式工作流包**，只包含指令、触发策略、受控命令推荐、文档和测试用例。它不能包含可自动执行的 C#、Python、Dynamo、LISP 或 shell 代码。

- Skill 不能新增宿主命令；只能从已安装 Bridge 的命令白名单中推荐命令。
- 修改、创建、删除操作始终由宿主适配器 Dry-run，并要求用户在宿主 UI 中确认。
- 包下载后校验 SHA-256、发布者 Ed25519 签名、宿主与 Bridge 版本、所请求能力及撤销名单。
- 信任等级固定为 `官方内置`、`已验证发布者`、`社区已审核`、`本地未验证`。后两类默认不允许在企业策略下自动启用写入型工作流。
- 真正需要新增 Revit API/CAD .NET/RhinoCommon 能力的内容，应作为独立、签名的宿主插件发布，并走 Autodesk/Rhino 等各自安装机制；Skill 仅通过声明其依赖的“能力扩展”来关联它。

## 包格式

扩展名建议为 `.ytskill`，本质为 ZIP。根目录必须包含 `skill.json`，可选文件为 `README.md`、`CHANGELOG.md`、`icon.png` 和 `tests/cases.json`。完整示例见 `docs/skill-hub-catalog.example.json`；字段契约见 `docs/skill-hub-manifest.schema.json`。

关键字段：

- `id`：稳定的反向域名 ID，例如 `net.ytszkj.revit.quantity-audit`。
- `hosts`：宿主 ID、最低/最高软件版本、最低 Bridge 版本、所需能力扩展。
- `triggers`：触发词、意图标签与负触发词，用于按需加载。
- `commandProfiles`：每个宿主的推荐命令与读/写风险；客户端仍会与本地白名单求交集。
- `publisher`、`integrity`、`signature`：版本、来源与签名的可验证链路。
- `testMatrix`：声明已在哪些宿主版本和 Bridge 版本通过测试。

## 服务接口

建议将 Skill Hub 部署在 `ytszkj.net` 的独立子域，例如 `skills.ytszkj.net`。客户端只需要以下接口：

```text
GET  /api/v1/skills?host=revit&hostVersion=2027&bridgeVersion=1.0.0
GET  /api/v1/skills/{id}/versions
GET  /api/v1/packages/{id}/{version}
GET  /api/v1/revocations
POST /api/v1/telemetry/install             # 可关闭，匿名安装/兼容性结果
POST /api/v1/reviews                       # 登录后提交评价或问题
```

发布者控制台单独提供上传、静态扫描、签名、测试矩阵、人工审核、灰度发布、撤销和版本回滚。企业私库可实现兼容接口，或由客户端读取已签名的目录 JSON。

## 首批目录

1. Revit 模型查询与定位。
2. Revit 数量/明细表核对。
3. Revit 建模安全计划。
4. Revit 图纸与交付检查。
5. 呆猫精装工作流适配。
6. AutoCAD 图层、对象统计与标准检查。
7. Rhino 图层、对象质量与批量建模。
8. 企业 BIM 标准、命名规则和交付检查私有 Skill。

## 落地节奏

1. 当前版本：Skill 元数据、按需选择、内置 Skill 分类与触发词已在 Bridge 中落地。
2. 1.1：加入 `.ytskill` 本地导入/导出、签名校验、已安装页与离线目录。
3. 1.2：上线只读发现目录与已验证发布者，先免费分发官方和合作伙伴 Skill。
4. 1.3：发布者工作台、企业私库、测试矩阵、审核与撤销机制。
5. 2.0：在用户授权下引入可签名的能力扩展，并提供项目级权限策略与审计报表。

