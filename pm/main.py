import os
import html
import uuid
import logging
import shutil
from pathlib import Path
from fastapi import FastAPI, Request, Depends, File, UploadFile, HTTPException
from fastapi.responses import HTMLResponse, FileResponse
from fastapi.staticfiles import StaticFiles
from fastapi.templating import Jinja2Templates
from sqlalchemy.orm import Session
from database import engine, SessionLocal, get_db
import models

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s %(levelname)s %(name)s: %(message)s",
)
logger = logging.getLogger(__name__)

models.Base.metadata.create_all(bind=engine)

app = FastAPI()

BASE_DIR = Path(__file__).parent
STATIC_DIR = BASE_DIR / "static"
UPLOAD_DIR = STATIC_DIR / "uploads"
THUMBNAIL_DIR = STATIC_DIR / "thumbnails"
UPLOAD_DIR.mkdir(parents=True, exist_ok=True)
THUMBNAIL_DIR.mkdir(parents=True, exist_ok=True)

MAX_FILE_SIZE = 50 * 1024 * 1024  # 50MB
ALLOWED_MIME_PREFIXES = ("image/jpeg", "image/png", "image/gif", "image/webp", "image/")

app.mount("/static", StaticFiles(directory=str(STATIC_DIR)), name="static")
templates = Jinja2Templates(directory=str(BASE_DIR / "templates"))


def create_thumbnail(source_path: Path, thumb_path: Path, size: int = 400):
    from PIL import Image, ImageOps
    with Image.open(source_path) as img:
        img = ImageOps.exif_transpose(img)
        img = img.convert("RGB")
        img = ImageOps.fit(img, (size, size), Image.LANCZOS)
        img.save(thumb_path, "JPEG", quality=82, optimize=True)


def get_thumbnail_path(photo_id: int, original_path: str) -> Path:
    thumb_path = THUMBNAIL_DIR / f"{photo_id}.jpg"
    if not thumb_path.exists():
        create_thumbnail(Path(original_path), thumb_path)
    return thumb_path


def query_photos_ordered(db: Session):
    return db.query(models.Photo).order_by(models.Photo.uploaded_at.desc()).all()


def render_card(photo) -> str:
    safe_title = html.escape(photo.title)
    uploaded = photo.uploaded_at.strftime("%Y-%m-%d %H:%M") if photo.uploaded_at else ""
    return f"""<div class="photo-card group cursor-pointer select-none"
     data-photo-id="{photo.id}"
     data-photo-title="{safe_title}"
     data-photo-date="{uploaded}"
     onclick="openPreview(this)">
    <div class="aspect-square overflow-hidden rounded-xl bg-base-300 shadow-sm relative">
        <img src="/pm/thumbnail/{photo.id}"
             alt="{safe_title}"
             class="object-cover w-full h-full transition-transform duration-300 group-hover:scale-105"
             loading="lazy" />
        <div class="absolute inset-0 bg-black/0 group-hover:bg-black/20 transition-colors duration-200 flex items-center justify-center">
            <span class="text-white text-2xl opacity-0 group-hover:opacity-100 transition-opacity duration-200 click-hint">🔍</span>
        </div>
    </div>
    <p class="text-xs mt-1 truncate text-base-content/60 px-0.5">{safe_title}</p>
</div>"""


@app.get("/", response_class=HTMLResponse)
async def read_root(request: Request, db: Session = Depends(get_db)):
    photos = query_photos_ordered(db)
    cards_html = "".join(render_card(p) for p in photos)
    count = len(photos)
    return templates.TemplateResponse(
        request=request, name="index.html",
        context={"initial_cards": cards_html, "initial_count": count}
    )


@app.get("/photos", response_class=HTMLResponse)
async def list_photos(request: Request, db: Session = Depends(get_db)):
    photos = query_photos_ordered(db)
    content = "".join(render_card(p) for p in photos)
    count = len(photos)
    count_label = "张照片" if count else ""
    oob = f'<span id="photo-count" hx-swap-oob="true">{count} {count_label}</span>'
    return HTMLResponse(content=content + oob)


@app.post("/upload", response_class=HTMLResponse)
async def upload_photo(file: UploadFile = File(...), db: Session = Depends(get_db)):
    if not file.content_type or not file.content_type.startswith("image/"):
        raise HTTPException(status_code=400, detail="画像ファイルのみアップロード可能です")

    data = await file.read()
    if len(data) > MAX_FILE_SIZE:
        raise HTTPException(status_code=413, detail=f"ファイルサイズが上限（50MB）を超えています")
    if len(data) == 0:
        raise HTTPException(status_code=400, detail="空のファイルはアップロードできません")

    ext = os.path.splitext(file.filename or "photo")[1].lower() or ".jpg"
    filename = f"{uuid.uuid4()}{ext}"
    file_path = UPLOAD_DIR / filename

    try:
        with open(file_path, "wb") as buffer:
            buffer.write(data)
    except OSError as e:
        logger.error("ファイル保存失敗: %s", e)
        raise HTTPException(status_code=500, detail="ファイルの保存に失敗しました")

    db_photo = models.Photo(title=file.filename or filename, filename=filename, file_path=str(file_path))
    db.add(db_photo)
    try:
        db.commit()
        db.refresh(db_photo)
    except Exception as e:
        logger.error("DB保存失敗: %s", e)
        file_path.unlink(missing_ok=True)
        raise HTTPException(status_code=500, detail="データベースへの保存に失敗しました")

    try:
        get_thumbnail_path(db_photo.id, str(file_path))
    except Exception as e:
        logger.warning("サムネイル生成失敗 photo_id=%d: %s", db_photo.id, e)

    logger.info("アップロード完了: %s (id=%d)", file.filename, db_photo.id)
    return HTMLResponse(content=render_card(db_photo))


@app.get("/thumbnail/{photo_id}")
async def get_thumbnail(photo_id: int, db: Session = Depends(get_db)):
    photo = db.query(models.Photo).filter(models.Photo.id == photo_id).first()
    if not photo:
        raise HTTPException(status_code=404, detail="Photo not found")
    try:
        thumb = get_thumbnail_path(photo_id, photo.file_path)
        return FileResponse(str(thumb), media_type="image/jpeg")
    except Exception as e:
        logger.warning("サムネイル取得失敗 photo_id=%d: %s", photo_id, e)
        if Path(photo.file_path).exists():
            return FileResponse(photo.file_path)
        raise HTTPException(status_code=404, detail="画像ファイルが見つかりません")


@app.get("/photo/{photo_id}")
async def get_photo_detail(photo_id: int, db: Session = Depends(get_db)):
    photo = db.query(models.Photo).filter(models.Photo.id == photo_id).first()
    if not photo:
        raise HTTPException(status_code=404, detail="Photo not found")

    file_path = Path(photo.file_path)
    file_size = None
    width = None
    height = None
    fmt = None

    if file_path.exists():
        file_size = file_path.stat().st_size
        try:
            from PIL import Image
            with Image.open(file_path) as img:
                width, height = img.size
                fmt = img.format
        except Exception as e:
            logger.warning("画像情報取得失敗 photo_id=%d: %s", photo_id, e)

    return {
        "id": photo.id,
        "title": photo.title,
        "filename": photo.filename,
        "uploaded_at": photo.uploaded_at.strftime("%Y-%m-%d %H:%M:%S") if photo.uploaded_at else None,
        "file_size": file_size,
        "width": width,
        "height": height,
        "format": fmt,
    }


@app.get("/download/{photo_id}")
async def download_photo(photo_id: int, db: Session = Depends(get_db)):
    photo = db.query(models.Photo).filter(models.Photo.id == photo_id).first()
    if not photo:
        raise HTTPException(status_code=404, detail="Photo not found")
    if not Path(photo.file_path).exists():
        raise HTTPException(status_code=404, detail="画像ファイルが見つかりません")
    return FileResponse(photo.file_path, filename=photo.title)


@app.delete("/delete/{photo_id}")
async def delete_photo(photo_id: int, db: Session = Depends(get_db)):
    photo = db.query(models.Photo).filter(models.Photo.id == photo_id).first()
    if not photo:
        raise HTTPException(status_code=404, detail="Photo not found")

    for path_str in [photo.file_path, str(THUMBNAIL_DIR / f"{photo_id}.jpg")]:
        p = Path(path_str)
        if p.exists():
            try:
                p.unlink()
            except OSError as e:
                logger.warning("ファイル削除失敗 %s: %s", path_str, e)

    db.delete(photo)
    db.commit()
    logger.info("削除完了: photo_id=%d", photo_id)
    return HTMLResponse(content="")
