---
name: troubleshooting, dsai, fastapi, caddy, deployment
description: The service at https://ai.0101.click/dsai was restored by restarting 'dsai.servi...
type: user
userId: 1
tags: troubleshooting, dsai, fastapi, caddy, deployment
relations: dsai.service,FastAPI,Caddy,https://ai.0101.click/dsai
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-26T10:20:40.1229435Z
lastAccessedAt: 2026-05-26T10:20:40.1229436Z
---

The service at https://ai.0101.click/dsai was restored by restarting 'dsai.service' (port 8002) and configuring FastAPI with 'root_path="/dsai"' to ensure correct URL generation through the Caddy reverse proxy.