---
name: pm,auth,bcrypt,passlib,python,bug
description: pmアプリ(auth.py)のパスワードハッシュはpasslibを使わずbcryptを直接使用する。bcrypt 5.x とpasslib 1.7.4の非互換性...
type: user
userId: 1
tags: pm,auth,bcrypt,passlib,python,bug
relevanceScore: 80
accessCount: 4
createdAt: 2026-04-27T14:18:39.0195310Z
lastAccessedAt: 2026-04-28T12:28:15.6829416Z
---

pmアプリ(auth.py)のパスワードハッシュはpasslibを使わずbcryptを直接使用する。bcrypt 5.x とpasslib 1.7.4の非互換性が原因で登録が失敗した。