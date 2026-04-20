import os
import shutil
from fastapi import FastAPI, Request, UploadFile, File, Depends, HTTPException
from fastapi.responses import HTMLResponse, FileResponse
from fastapi.staticfiles import StaticFiles
from fastapi.templating import Jinja2Templates
from sqlalchemy.orm import Session
import models, database

# Create tables
models.Base.metadata.create_all(bind=database.engine)

app = FastAPI()

# Mount static files and templates
UPLOAD_DIR = "static/uploads"
os.makedirs(UPLOAD_DIR, exist_ok=True)
app.mount("/static", StaticFiles(directory="static"), name="static")
templates = Jinja2Templates(directory="templates")

@app.get("/", response_class=HTMLResponse)
async def read_root(request: Request):
    return templates.TemplateResponse("index.html", {"request": request})

@app.get("/photos", response_class=HTMLResponse)
async def list_photos(request: Request, db: Session = Depends(database.get_db)):
    photos = db.query(models.Photo).order_by(models.Photo.upload_date.desc()).all()
    # Simplified HTML fragment for HTMX
    html_content = ""
    for photo in photos:
        html_content += f"""
        <div class="bg-white p-4 rounded-lg shadow-md flex flex-col items-center">
            <img src="/{photo.filepath}" class="w-full h-48 object-cover rounded-md mb-2 cursor-pointer" 
                 onclick="window.open('/download/{photo.id}')">
            <div class="text-sm font-medium mb-2 truncate w-full text-center">{photo.filename}</div>
            <button hx-delete="/delete/{photo.id}" 
                    hx-target="#photo-list" 
                    hx-confirm="Are you sure?"
                    class="bg-red-500 hover:bg-red-600 text-white px-3 py-1 rounded text-xs transition duration-200">
                Delete
            </button>
        </div>
        """
    return HTMLResponse(content=html_content if html_content else "<p class='col-span-full text-center text-gray-500'>No photos uploaded yet.</p>")

@app.post("/upload")
async def upload_photo(file: UploadFile = File(...), db: Session = Depends(database.get_db)):
    file_path = os.path.join(UPLOAD_DIR, file.filename)
    
    # Save file to disk
    with open(file_path, "wb") as buffer:
        shutil.copyfileobj(file.file, buffer)
    
    # Save to database
    db_photo = models.Photo(filename=file.filename, filepath=file_path)
    db.add(db_photo)
    db.commit()
    
    # Trigger HTMX refresh of the photo list
    return HTMLResponse(status_code=204, headers={"HX-Trigger": "photoListChanged"})

@app.get("/download/{photo_id}")
async def download_photo(photo_id: int, db: Session = Depends(database.get_db)):
    photo = db.query(models.Photo).filter(models.Photo.id == photo_id).first()
    if not photo:
        raise HTTPException(status_code=404, detail="Photo not found")
    return FileResponse(photo.filepath, filename=photo.filename)

@app.delete("/delete/{photo_id}")
async def delete_photo(photo_id: int, db: Session = Depends(database.get_db)):
    photo = db.query(models.Photo).filter(models.Photo.id == photo_id).first()
    if not photo:
        raise HTTPException(status_code=404, detail="Photo not found")
    
    # Delete file from disk
    if os.path.exists(photo.filepath):
        os.remove(photo.filepath)
        
    db.delete(photo)
    db.commit()
    
    # Return updated list after deletion
    return await list_photos(None, db) # Mock request is okay here as we don't use it in list_photos
