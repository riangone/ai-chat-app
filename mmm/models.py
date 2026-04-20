from sqlalchemy import Column, Integer, String, DateTime
from datetime import datetime
from .database import Base

class Photo(Base):
    __tablename__ = "photos"

    id = Column(Integer, primary_key=True, index=True)
    filename = Column(String)
    filepath = Column(String)
    upload_date = Column(DateTime, default=datetime.utcnow)
