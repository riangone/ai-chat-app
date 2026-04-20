# Photo Management System (PMS) - Design Document

This document outlines the architecture and design for a lightweight, efficient photo management system.

## 1. Technical Stack
- **Backend:** FastAPI (Python 3.10+)
- **Frontend:** HTMX + Tailwind CSS + DaisyUI
- **Database:** SQLite (SQLAlchemy ORM)
- **Image Processing:** Pillow (PIL)
- **Deployment:** Uvicorn / Docker

## 2. Folder Structure Design
```text
pm/
├── main.py              # FastAPI application entry point
├── models.py            # SQLAlchemy models
├── database.py          # Database connection and session management
├── schemas.py           # Pydantic models for API validation
├── crud.py              # Database CRUD operations
├── routes/              # Modular API routes
│   ├── auth.py          # Authentication (Login/Register)
│   ├── photos.py        # Photo management (Upload/Delete/Update)
│   └── albums.py        # Album management
├── static/              # Static files (CSS, JS)
│   └── uploads/         # User-uploaded images (ignored by git)
│       ├── original/    # High-resolution originals
│       └── thumbnails/  # Generated thumbnails
├── templates/           # Jinja2 templates for HTMX
│   ├── base.html
│   ├── components/      # Reusable UI components
│   └── pages/           # Full page views
└── requirements.txt     # Project dependencies
```

## 3. Database Schema (SQLite)

### Users Table
| Column | Type | Description |
| :--- | :--- | :--- |
| id | Integer (PK) | Unique user ID |
| username | String | Unique username |
| hashed_password | String | BCrypt hashed password |
| created_at | DateTime | Account creation timestamp |

### Photos Table
| Column | Type | Description |
| :--- | :--- | :--- |
| id | Integer (PK) | Unique photo ID |
| user_id | Integer (FK) | Owner of the photo |
| title | String | User-provided title |
| description | Text | Optional description |
| file_path | String | Path to original file |
| thumb_path | String | Path to thumbnail |
| uploaded_at | DateTime | Upload timestamp |
| album_id | Integer (FK) | Optional album link |

### Albums Table
| Column | Type | Description |
| :--- | :--- | :--- |
| id | Integer (PK) | Unique album ID |
| user_id | Integer (FK) | Owner of the album |
| name | String | Album name |
| description | Text | Album description |
| created_at | DateTime | Creation timestamp |

## 4. Core API Endpoints Design

### Frontend / View Endpoints (HTMX)
| Method | Endpoint | Description |
| :--- | :--- | :--- |
| GET | `/` | Home page (Gallery view) |
| GET | `/login` | Login page |
| GET | `/register` | Registration page |
| GET | `/photos/upload` | Upload form component |

### Functional API Endpoints
| Method | Endpoint | Description |
| :--- | :--- | :--- |
| POST | `/api/auth/login` | Authenticate user and set cookie |
| POST | `/api/auth/logout` | Clear session cookie |
| GET | `/api/photos` | List photos (returns HTMX gallery items) |
| POST | `/api/photos/upload` | Process image upload and generate thumbnail |
| DELETE | `/api/photos/{id}` | Delete photo and remove files |
| PATCH | `/api/photos/{id}` | Update photo metadata (title/description) |
| GET | `/api/albums` | List user albums |
| POST | `/api/albums` | Create a new album |

## 5. UI/UX Considerations
- **Infinite Scroll:** Use HTMX `hx-get` with `hx-trigger="revealed"` for the gallery.
- **Lazy Loading:** Native `loading="lazy"` for thumbnails.
- **Modals:** Use DaisyUI modals for photo detail views and upload forms.
- **Responsiveness:** Grid layout using Tailwind (`grid-cols-1 md:grid-cols-3 lg:grid-cols-4`).
