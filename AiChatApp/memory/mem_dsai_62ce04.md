---
name: dsai scripts service-management
description: dsai项目在/home/ubuntu/ws/dsai/目录下有三个服务管理脚本，restart.sh包含caddy重载和pyc清理
type: project
---

dsai项目在/home/ubuntu/ws/dsai/目录下有三个服务管理脚本：restart.sh、stop.sh、start.sh。

restart.sh内容包含：
1. 清理Python .pyc缓存文件
2. `sudo systemctl daemon-reload`
3. `sudo systemctl restart dsai`
4. `sudo systemctl reload caddy`（确保Caddy代理也刷新）

**Why:** 服务重启后如果Caddy未重载，可能仍代理旧版响应。
**How to apply:** 每次dsai代码变更后运行`./restart.sh`即可完成全量刷新。