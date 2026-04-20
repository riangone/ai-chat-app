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

    # 2. 创建项目
    now = datetime.now().isoformat()
    cursor.execute("""
        INSERT INTO Projects (Name, RootPath, UserId, CreatedAt)
        VALUES ('Multi-Agent Test Case', '/home/ubuntu/ws/ai-chat-app/AiChatApp/test-project', ?, ?)
    """, (user_id, now))
    project_id = cursor.lastrowid

    # 3. 创建代理角色
    agents = [
        ('Architect-Agent', 'You are a system architect.', project_id, 1, '#4A90E2'),
        ('Developer-Agent', 'You are a senior developer.', project_id, 1, '#50E3C2')
    ]
    for agent in agents:
        cursor.execute("""
            INSERT INTO AgentProfiles (RoleName, SystemPrompt, ProjectId, IsActive, Color)
            VALUES (?, ?, ?, ?, ?)
        """, agent)

    # 4. 创建会话
    cursor.execute("""
        INSERT INTO ChatSessions (Title, UserId, ProjectId, CreatedAt)
        VALUES ('Multi-Agent Test Session', ?, ?, ?)
    """, (user_id, project_id, now))
    session_id = cursor.lastrowid

    # 5. 创建消息
    cursor.execute("""
        INSERT INTO Messages (ChatSessionId, Content, IsAi, Timestamp)
        VALUES (?, 'Run multi-agent collaboration test.', 0, ?)
    """, (session_id, now))
    message_id = cursor.lastrowid

    # 6. 创建步骤
    steps = [
        (message_id, 'Architect-Agent', 'Architect', 'Define system structure', 'Approved structure: Controller -> Service -> DB', 1, 1, 1500, now),
        (message_id, 'Developer-Agent', 'Developer', 'Implement models', 'Models created: User.cs, Project.cs', 1, 1, 3200, now)
    ]

    for step in steps:
        cursor.execute("""
            INSERT INTO AgentSteps (MessageId, Role, Persona, Input, Output, AttemptNumber, WasAccepted, DurationMs, CreatedAt)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
        """, step)

    conn.commit()
    conn.close()
    print(f"Success! Project ID: {project_id}, Session ID: {session_id}")

if __name__ == "__main__":
    setup_test_data()
