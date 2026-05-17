---
name: frontend,swiper,ui-ux,bug-fix
description: In the AiChatApp project, the main Swiper instance in `wwwroot/index.html` was u...
type: user
userId: 1
tags: frontend,swiper,ui-ux,bug-fix
relations: Hyperion,AiChatApp,Swiper,wwwroot/index.html
relevanceScore: 80
accessCount: 46
createdAt: 2026-05-12T15:10:41.9539926Z
lastAccessedAt: 2026-05-17T23:14:28.0465898Z
---

In the AiChatApp project, the main Swiper instance in `wwwroot/index.html` was updated with `simulateTouch: false` to disable mouse-driven swiping on desktop, ensuring text selection doesn't trigger unintended slides while preserving touch swiping for mobile.