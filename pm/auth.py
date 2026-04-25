import os
from fastapi import Request, HTTPException
from fastapi.responses import RedirectResponse
from itsdangerous import URLSafeTimedSerializer, BadSignature, SignatureExpired
from passlib.context import CryptContext
from sqlalchemy.orm import Session
import models

SECRET_KEY = os.environ.get("SECRET_KEY", "photomanager-secret-key-change-in-prod")
SESSION_COOKIE = "pm_session"
SESSION_MAX_AGE = 60 * 60 * 24 * 7  # 7日

pwd_context = CryptContext(schemes=["bcrypt"], deprecated="auto")
serializer = URLSafeTimedSerializer(SECRET_KEY)


def hash_password(password: str) -> str:
    return pwd_context.hash(password)


def verify_password(plain: str, hashed: str) -> bool:
    return pwd_context.verify(plain, hashed)


def create_session_token(user_id: int) -> str:
    return serializer.dumps(user_id, salt="session")


def decode_session_token(token: str) -> int | None:
    try:
        return serializer.loads(token, salt="session", max_age=SESSION_MAX_AGE)
    except (BadSignature, SignatureExpired):
        return None


def get_current_user(request: Request, db: Session) -> models.User:
    token = request.cookies.get(SESSION_COOKIE)
    if not token:
        raise HTTPException(status_code=401, detail="Not authenticated")
    user_id = decode_session_token(token)
    if not user_id:
        raise HTTPException(status_code=401, detail="Invalid or expired session")
    user = db.query(models.User).filter(models.User.id == user_id).first()
    if not user:
        raise HTTPException(status_code=401, detail="User not found")
    return user


def get_current_user_or_redirect(request: Request, db: Session) -> models.User:
    try:
        return get_current_user(request, db)
    except HTTPException:
        return None


def login_required(request: Request, db: Session) -> models.User:
    user = get_current_user_or_redirect(request, db)
    if user is None:
        raise HTTPException(status_code=302, headers={"Location": "/login"})
    return user
