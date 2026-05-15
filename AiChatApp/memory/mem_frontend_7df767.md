---
name: frontend,swiper,ui-ux,bug-fix
description: In the AiChatApp project, the main Swiper instance in `wwwroot/index.html` was u...
type: user
userId: 1
tags: frontend,swiper,ui-ux,bug-fix
relations: Hyperion,AiChatApp,Swiper,wwwroot/index.html
relevanceScore: 80
accessCount: 16
createdAt: 2026-05-12T15:10:41.9539926Z
lastAccessedAt: 2026-05-14T23:46:35.3279867Z
---

In the AiChatApp project, the main Swiper instance in `wwwroot/index.html` was updated with `simulateTouch: false` to disable mouse-driven swiping on desktop, ensuring text selection doesn't trigger unintended slides while preserving touch swiping for mobile.