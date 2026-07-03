---
name: 用户偏好,UI设计,视觉风格
description: ### 用户偏好整合汇总

1. **语言与交流偏好**
   - 用户偏好使用中文进行交流沟通。

2. **界面与设计偏好 (Polished UI)**
...
type: user
userId: 1
tags: 用户偏好,UI设计,视觉风格
relations: 用户偏好,Polished UI,NetYamlForge,diary-companion,Quiz,DiaryForm
relevanceScore: 90
accessCount: 5
createdAt: 2026-07-02T14:13:34.3444875Z
lastAccessedAt: 2026-07-02T22:17:05.7361448Z
boundAgentRole: 
---

### 用户偏好整合汇总

1. **语言与交流偏好**
   - 用户偏好使用中文进行交流沟通。

2. **界面与设计偏好 (Polished UI)**
   - 要求所有用户界面 (UI) 都使用精致且美观的UI设计风格 (Polished UI)。
   - 对页面流量消耗高度敏感，期望在极致省流的前提下，保证缩略图的清晰度与页面的整体美观。

3. **开发与构建偏好**
   - 偏好基于 NetYamlForge 框架的低代码 (low-code) 模式开发项目。
   - 习惯参考已有项目 (如 diary-companion) 的模式进行新项目的构建。

4. **单词学习挑战 (Quiz) 规则与功能**
   - **语言过滤**：挑战时必须固定使用系统当前/默认选择的界面语言 (与 zh-CN/en-US/ja-JP/ko-KR 语言切换器一致)，而不是图片上传/标注时指定的语言。该逻辑已在前端 `views/Quiz.cshtml` 中通过 `systemLanguage` 过滤单词实现。
   - **特定图片选择**：支持用户在单词挑战中选择特定图片进行答题。该功能已在 `views/Quiz.cshtml` 中通过 `pv-photo-picker` 图片选择条实现，用户可选择特定图片或全部图片进行挑战，切换时会重置统计数据。

5. **日记布局与展现**
   - 主页日记卡片流需按照时间降序 (最新的日记排在最上方) 展示。

6. **界面组件细节要求**
   - **日记表单 (DiaryForm) 页面**：需移除“记录我的心路历程”的标题，并添加用于返回日记一览页面的按钮或链接。
   - **悬浮按钮组**：需移除“设置”按钮 (因为侧边栏中已包含该功能)。