#!/usr/bin/env python3
import os
import sqlite3
import subprocess
from datetime import datetime, timezone, timedelta

def get_git_commits(workspace_path):
    """Scans all git repositories in the workspace and extracts today's commits by the user."""
    commits = []
    if not os.path.exists(workspace_path):
        return commits

    # Get local timezone offset (since we want "today" in local time)
    # The current local time is JST (UTC+9), but let's make it robust
    now = datetime.now()
    since_date = now.strftime("%Y-%m-%d 00:00:00")

    for item in os.listdir(workspace_path):
        repo_path = os.path.join(workspace_path, item)
        if os.path.isdir(repo_path) and os.path.exists(os.path.join(repo_path, ".git")):
            try:
                # Get the repository name
                repo_name = item
                # Run git log for today's commits by the current git user
                cmd = [
                    "git", "log",
                    f"--since={since_date}",
                    "--pretty=format:%s (%h)",
                    "--author=" + subprocess.check_output(["git", "config", "user.name"], cwd=repo_path).decode().strip()
                ]
                output = subprocess.check_output(cmd, cwd=repo_path, stderr=subprocess.DEVNULL).decode().strip()
                if output:
                    repo_commits = output.split("\n")
                    commits.append((repo_name, repo_commits))
            except Exception:
                # Ignore errors for non-git dirs or git command issues
                continue
    return commits

def get_completed_tasks(db_path):
    """Queries the SQLite database for tasks completed today."""
    tasks = []
    if not os.path.exists(db_path):
        return tasks

    try:
        conn = sqlite3.connect(db_path)
        cursor = conn.cursor()
        
        # Get tasks that are completed and created/due today
        # In SQLite, dates are stored as text.
        today_str = datetime.now().strftime("%Y-%m-%d")
        
        # We can look for completed tasks
        cursor.execute(
            "SELECT Title, Description, DueDate FROM TodoItems WHERE IsCompleted = 1 AND CreatedAt LIKE ?",
            (f"{today_str}%",)
        )
        for row in cursor.fetchall():
            tasks.append({
                "title": row[0],
                "description": row[1] or "",
                "due_date": row[2] or ""
            })
        conn.close()
    except Exception as e:
        tasks.append({"title": f"Error querying tasks: {str(e)}", "description": "", "due_date": ""})
    
    return tasks

def generate_report():
    workspace = "/home/ubuntu/ws"
    db_path = "/home/ubuntu/ws/ai-chat-app/AiChatApp/chat.db"
    
    now_str = datetime.now().strftime("%Y年%m月%d日 %H:%M")
    print(f"# 每日工作简报 (Timesheet Summary)")
    print(f"**生成时间**: {now_str}\n")
    
    # 1. Git Commits
    print("## 💻 今日代码提交 (Git Commits)")
    commits = get_git_commits(workspace)
    if not commits:
        print("今日无 Git 代码提交记录。")
    else:
        for repo, repo_commits in commits:
            print(f"### 📦 项目: {repo}")
            for c in repo_commits:
                print(f"- {c}")
            print()
            
    # 2. Completed Tasks
    print("## 📝 今日完成的任务 (Completed Tasks)")
    tasks = get_completed_tasks(db_path)
    if not tasks:
        # Also query active tasks as reference
        try:
            conn = sqlite3.connect(db_path)
            cursor = conn.cursor()
            cursor.execute("SELECT Title FROM TodoItems WHERE IsCompleted = 0 LIMIT 5")
            active_rows = cursor.fetchall()
            conn.close()
            if active_rows:
                print("今日无已完成任务。以下是当前进行中的任务：")
                for row in active_rows:
                    print(f"- [ ] {row[0]}")
            else:
                print("今日无任务记录。")
        except Exception:
            print("今日无已完成任务记录。")
    else:
        for t in tasks:
            desc = f" ({t['description']})" if t['description'] else ""
            print(f"- [x] **{t['title']}**{desc}")
    print()

    # 3. Timesheet Draft Table
    print("## 🕒 工时填报建议表格")
    print("| 项目名称 | 工作内容 | 工时 (小时) | 状态 |")
    print("| :--- | :--- | :---: | :--- |")
    
    has_items = False
    if commits:
        for repo, _ in commits:
            print(f"| {repo} | 代码开发与调试 | 4.0 | 已完成 |")
            has_items = True
    if tasks:
        print(f"| AiChatApp | 待办任务跟进与处理 | 3.0 | 已完成 |")
        has_items = True
    if not has_items:
        print("| [项目名称] | [工作内容描述] | 8.0 | 已完成 |")
    print()

if __name__ == "__main__":
    generate_report()
