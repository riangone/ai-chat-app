import sqlite3
from datetime import datetime

def setup_test_data():
    conn = sqlite3.connect('AiChatApp/chat.db')
    cursor = conn.cursor()

    # 1. 获取用户
    cursor.execute("SELECT Id FROM Users LIMIT 1")
    user = cursor.fetchone()
    if not user:
        print("Error: No users found.")
        return
    user_id = user[0]

    # 2. 创建新项目 (FileManager)
    now = datetime.now().isoformat()
    cursor.execute("""
        INSERT INTO Projects (Name, RootPath, UserId, CreatedAt)
        VALUES ('FileManager App Development', '/home/ubuntu/ws/test/filemanager', ?, ?)
    """, (user_id, now))
    project_id = cursor.lastrowid

    # 3. 创建代理角色
    agents = [
        ('API-Architect', 'Expert in FastAPI and system design.', project_id, 1, '#FF5733'),
        ('UI-Specialist', 'Expert in HTMX and Tailwind CSS.', project_id, 1, '#33FF57')
    ]
    for agent in agents:
        cursor.execute("""
            INSERT INTO AgentProfiles (RoleName, SystemPrompt, ProjectId, IsActive, Color)
            VALUES (?, ?, ?, ?, ?)
        """, agent)

    # 4. 创建会话
    cursor.execute("""
        INSERT INTO ChatSessions (Title, UserId, ProjectId, CreatedAt)
        VALUES ('Project: FileManager Implementation', ?, ?, ?)
    """, (user_id, project_id, now))
    session_id = cursor.lastrowid

    # 5. 用户原始请求
    cursor.execute("""
        INSERT INTO Messages (ChatSessionId, Content, IsAi, Timestamp)
        VALUES (?, 'Create a file manager app using FastAPI and HTMX to browse directories.', 0, ?)
    """, (session_id, now))
    user_msg_id = cursor.lastrowid

    # 6. 协作轨迹 (AgentSteps)
    steps = [
        (user_msg_id, 'API-Architect', 'Architect', 'Backend Routing', 'Designed FastAPI endpoints for directory traversal and file listing.', 1, 1, 1800, now),
        (user_msg_id, 'UI-Specialist', 'Frontend Dev', 'HTMX Template', 'Created index.html with HTMX for dynamic content loading and Tailwind for styling.', 1, 1, 3500, now)
    ]
    for step in steps:
        cursor.execute("""
            INSERT INTO AgentSteps (MessageId, Role, Persona, Input, Output, AttemptNumber, WasAccepted, DurationMs, CreatedAt)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
        """, step)

    # 7. AI 总结回复
    summary = "FileManager 应用已成功搭建。API-Architect 完成了 FastAPI 的后端路由设计，UI-Specialist 实现了基于 HTMX 和 Tailwind 的动态前端。所有代码均已部署在 `/home/ubuntu/ws/test/filemanager`。"
    cursor.execute("""
        INSERT INTO Messages (ChatSessionId, Content, IsAi, Timestamp)
        VALUES (?, ?, 1, ?)
    """, (session_id, summary, now))

    conn.commit()
    conn.close()
    print(f"FileManager Test Case created! Project ID: {project_id}")

if __name__ == "__main__":
    setup_test_data()
