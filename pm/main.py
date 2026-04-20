import os
import uuid
import shutil
from fastapi import FastAPI, Request, Depends, File, UploadFile, HTTPException
from fastapi.responses import HTMLResponse, FileResponse
from fastapi.staticfiles import StaticFiles
from fastapi.templating import Jinja2Templates
from sqlalchemy.orm import Session
from database import engine, SessionLocal, get_db
import models

# Initialize database
models.Base.metadata.create_all(bind=engine)

app = FastAPI()

# Mount static files
app.mount("/static", StaticFiles(directory="static"), name="static")

# Templates
templates = Jinja2Templates(directory="templates")

# Upload directory
UPLOAD_DIR = "static/uploads"
os.makedirs(UPLOAD_DIR, exist_ok=True)

@app.get("/", response_class=HTMLResponse)
async def read_root(request: Request):
    return templates.TemplateResponse("index.html", {"request": request})

@app.get("/photos", response_class=HTMLResponse)
async def list_photos(request: Request, db: Session = Depends(get_db)):
    photos = db.query(models.Photo).order_by(models.Photo.uploaded_at.desc()).all()
    html_content = ""
    for photo in photos:
        html_content += f"""
        <div class="card bg-base-100 shadow-xl overflow-hidden group">
            <figure class="relative aspect-square">
                <img src="/download/{photo.id}" alt="{photo.title}" class="object-cover w-full h-full" />
                <div class="absolute inset-0 bg-black bg-opacity-0 group-hover:bg-opacity-30 transition-all flex items-center justify-center opacity-0 group-hover:opacity-100">
                    <button class="btn btn-error btn-sm" hx-delete="/delete/{photo.id}" hx-target="closest .card" hx-swap="outerHTML" hx-confirm="Are you sure?">Delete</button>
                </div>
            </figure>
            <div class="card-body p-4">
                <h2 class="card-title text-sm truncate">{photo.title}</h2>
            </div>
        </div>
        """
    return HTMLResponse(content=html_content)

@app.post("/upload", response_class=HTMLResponse)
async def upload_photo(file: UploadFile = File(...), db: Session = Depends(get_db)):
    if not file.content_type.startswith("image/"):
        raise HTTPException(status_code=400, detail="File must be an image")
    
    filename = f"{uuid.uuid4()}_{file.filename}"
    file_path = os.path.join(UPLOAD_DIR, filename)
    
    with open(file_path, "wb") as buffer:
        shutil.copyfileobj(file.file, buffer)
    
    db_photo = models.Photo(title=file.filename, filename=filename, file_path=file_path)
    db.add(db_photo)
    db.commit()
    db.refresh(db_photo)
    
    # Return the HTMX snippet for the new photo
    return HTMLResponse(content=f"""
    <div class="card bg-base-100 shadow-xl overflow-hidden group">
        <figure class="relative aspect-square">
            <img src="/download/{db_photo.id}" alt="{db_photo.title}" class="object-cover w-full h-full" />
            <div class="absolute inset-0 bg-black bg-opacity-0 group-hover:bg-opacity-30 transition-all flex items-center justify-center opacity-0 group-hover:opacity-100">
                <button class="btn btn-error btn-sm" hx-delete="/delete/{db_photo.id}" hx-target="closest .card" hx-swap="outerHTML" hx-confirm="Are you sure?">Delete</button>
            </div>
        </figure>
        <div class="card-body p-4">
            <h2 class="card-title text-sm truncate">{db_photo.title}</h2>
        </div>
    </div>
    """)

@app.get("/download/{photo_id}")
async def download_photo(photo_id: int, db: Session = Depends(get_db)):
    photo = db.query(models.Photo).filter(models.Photo.id == photo_id).first()
    if not photo:
        raise HTTPException(status_code=404, detail="Photo not found")
    return FileResponse(photo.file_path)

@app.delete("/delete/{photo_id}")
async def delete_photo(photo_id: int, db: Session = Depends(get_db)):
    photo = db.query(models.Photo).filter(models.Photo.id == photo_id).first()
    if not photo:
        raise HTTPException(status_code=404, detail="Photo not found")
    
    if os.path.exists(photo.file_path):
        os.remove(photo.file_path)
    
    db.delete(photo)
    db.commit()
    return HTMLResponse(content="")
