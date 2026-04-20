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

    # 2. 创建新项目 (Case 2)
    now = datetime.now().isoformat()
    cursor.execute("""
        INSERT INTO Projects (Name, RootPath, UserId, CreatedAt)
        VALUES ('Memory Search Service Implementation', '/home/ubuntu/ws/ai-chat-app/AiChatApp/test-project-2', ?, ?)
    """, (user_id, now))
    project_id = cursor.lastrowid

    # 3. 创建代理角色
    agents = [
        ('Logic-Architect', 'Focus on algorithm design.', project_id, 1, '#F5A623'),
        ('Backend-Dev', 'Focus on C# implementation.', project_id, 1, '#9013FE')
    ]
    for agent in agents:
        cursor.execute("""
            INSERT INTO AgentProfiles (RoleName, SystemPrompt, ProjectId, IsActive, Color)
            VALUES (?, ?, ?, ?, ?)
        """, agent)

    # 4. 创建会话
    cursor.execute("""
        INSERT INTO ChatSessions (Title, UserId, ProjectId, CreatedAt)
        VALUES ('Feature: Memory Search Logic', ?, ?, ?)
    """, (user_id, project_id, now))
    session_id = cursor.lastrowid

    # 5. 用户原始请求
    cursor.execute("""
        INSERT INTO Messages (ChatSessionId, Content, IsAi, Timestamp)
        VALUES (?, 'Implement the Memory Search Service with fuzzy matching.', 0, ?)
    """, (session_id, now))
    user_msg_id = cursor.lastrowid

    # 6. 协作轨迹 (AgentSteps)
    steps = [
        (user_msg_id, 'Logic-Architect', 'Architect', 'Search Strategy', 'Using keyword matching as a fallback for vector search.', 1, 1, 1200, now),
        (user_msg_id, 'Backend-Dev', 'Developer', 'Service Coding', 'Created MemorySearchService.cs with async search method.', 1, 1, 2800, now)
    ]
    for step in steps:
        cursor.execute("""
            INSERT INTO AgentSteps (MessageId, Role, Persona, Input, Output, AttemptNumber, WasAccepted, DurationMs, CreatedAt)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
        """, step)

    # 7. AI 总结回复 (确保聊天记录不为空)
    summary = "内存搜索服务实现已启动。Logic-Architect 已制定搜索策略，Backend-Dev 已在 `/Services` 目录下完成了核心逻辑的初步实现。您可以查看 `DESIGN_SPEC.md` 获取详细规范。"
    cursor.execute("""
        INSERT INTO Messages (ChatSessionId, Content, IsAi, Timestamp)
        VALUES (?, ?, 1, ?)
    """, (session_id, summary, now))

    conn.commit()
    conn.close()
    print(f"Test case 2 created! Project ID: {project_id}")

if __name__ == "__main__":
    setup_test_data()
