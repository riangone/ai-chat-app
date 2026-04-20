# Photo Management System (PMS) 用户指南

欢迎使用 **Photo Management System (PMS)**。这是一个基于 FastAPI 构建的轻量级、高性能照片管理系统，旨在为用户提供简洁、直观的照片上传、预览及管理体验。

## 1. 系统简介

PMS 采用现代 Web 技术栈开发，具备以下技术特点：
- **后端**：使用 FastAPI (Python 3.10+) 提供高性能异步接口。
- **前端**：采用 HTMX 实现无刷新交互，结合 Tailwind CSS 和 DaisyUI 构建响应式界面。
- **数据库**：使用 SQLite (SQLAlchemy ORM) 进行持久化存储。
- **核心功能**：支持照片的批量上传、实时预览、自动缩略图生成（设计中）以及便捷的删除管理。

## 2. 安装步骤

在开始运行系统之前，请确保您的环境中已安装 Python 3.10 或更高版本。

### 2.1 获取代码
进入项目目录：
```bash
cd /home/ubuntu/ws/ai-chat-app/pm
```

### 2.2 安装依赖
使用 `pip` 安装所需的 Python 库：
```bash
pip install -r requirements.txt
```
*主要依赖项包括：fastapi, uvicorn, sqlalchemy, jinja2, python-multipart 等。*

## 3. 运行指南

您可以使用 `uvicorn` 命令启动开发服务器。

### 3.1 启动服务器
在 `pm` 目录下运行以下命令：
```bash
uvicorn main:app --reload --host 0.0.0.0 --port 8000
```

### 3.2 访问系统
启动成功后，打开浏览器并访问：
- **主界面**：[http://localhost:8000](http://localhost:8000)
- **API 文档**：[http://localhost:8000/docs](http://localhost:8000/docs) (Swagger UI)

## 4. 功能使用说明

### 4.1 上传照片
1. 在主页面顶部找到上传区域。
2. 点击“选择文件”或拖拽图片文件到上传框。
3. 系统将自动处理上传，并在成功后通过 HTMX 将新照片动态添加到下方的照片墙中。
   - *注意：仅支持图片格式文件。*

### 4.2 预览照片
- 上传成功后的照片会以卡片形式展示在照片墙中。
- 您可以直接在页面上查看照片的缩略预览。
- 所有的照片都存储在 `static/uploads` 目录下。

### 4.3 删除照片
1. 将鼠标悬停在想要删除的照片卡片上。
2. 卡片中心会显示“Delete”（删除）按钮。
3. 点击删除按钮，并在弹出的确认框中选择“确定”。
4. 照片将从数据库和物理磁盘中同步删除，页面会自动移除该卡片。

---
*提示：本系统目前处于 v1.0 版本，更多高级功能（如相册管理、多用户登录）正在规划中。*
