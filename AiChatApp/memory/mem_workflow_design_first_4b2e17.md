---
name: design-first workflow preference
description: 用户在多代理任务中偏好先生成详细设计文档，再委托专门代理（如Python Expert）进行代码实现
type: feedback
---

用户明确要求在复杂功能实现时，先由 Architect 代理生成详细设计文档，然后委托其他专门代理（如 Python Expert）负责代码实现。

**Why:** 通过分工协作节省 Token 消耗，架构设计与代码实现由不同角色的代理分别完成，各司其职。
**How to apply:** 当用户要求实现多个功能或复杂系统时，先产出完整设计文档（docs/ 目录下的 .md 文件），明确接口定义、数据流和文件清单，再协调实现代理执行具体编码任务。不要跳过设计文档直接写代码。
