#!/bin/bash
# 以 YOLO 模式启动 Claude (跳过权限确认)
claude --dangerously-skip-permissions "$@"
