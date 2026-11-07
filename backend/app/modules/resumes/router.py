"""Resume / career documents: several files per user (resume, cover letter, LinkedIn export, portfolio …).

Only the extracted text is stored — never the original file. Documents can be deleted individually or all at once.
"""

from __future__ import annotations

from fastapi import APIRouter, File, Form, UploadFile, status

from app.core import rate_limit as rl
from app.core.deps import ContainerDep, CurrentUser
from app.core.errors import AppError, NotFound, PlanLimitExceeded
from app.core.utils import as_utc, new_id, utcnow
from app.modules.billing.service import BillingService
from app.modules.resumes.service import MAX_DOCUMENTS, InvalidResume, extract_text, sniff_type

router = APIRouter(prefix="/v1/resumes", tags=["resume"])


def public_document(doc: dict) -> dict:
    return {"id": doc["_id"], "filename": doc["filename"], "kind": doc.get("kind", "").lstrip("."),
            "char_count": doc["char_count"], "uploaded_at": as_utc(doc["created_at"]).isoformat(),
            "preview": doc["text"][:280]}


async def _documents(c, user_id: str) -> list[dict]:
    return await c.db.resumes.find({"user_id": user_id}).sort("created_at", 1).to_list(None)


async def _require_premium(c, user_id: str) -> None:
    plan = await BillingService(c.db).get_plan(user_id)
    if not plan.resume_questions:
        raise PlanLimitExceeded("Resume-based questions are a Premium feature.", code="plan_feature_locked",
                                details={"locked": ["resume-based questions"], "upgrade_available": True})


@router.post("", status_code=status.HTTP_201_CREATED)
async def upload_documents(
    user: CurrentUser,
    c: ContainerDep,
    files: list[UploadFile] | None = File(None, description="One or more PDF, DOCX or TXT files"),
    file: UploadFile | None = File(None, description="Single file (older clients)"),
    consent: bool = Form(..., description="User consents to processing the document text by AI"),
) -> dict:
    """Uploads one or more documents. All files are validated before anything is stored (all-or-nothing)."""
    rl.enforce(c.limiter, f"resume:{user['_id']}", rl.RESUME_UPLOAD_PER_USER)
    await _require_premium(c, user["_id"])
    if not consent:
        raise AppError("Consent is required to process your documents.", code="consent_required", status_code=422)

    uploads = [f for f in (files or []) if f is not None] + ([file] if file is not None else [])
    if not uploads:
        raise InvalidResume("Choose at least one file.")
    existing = await _documents(c, user["_id"])
    if len(existing) + len(uploads) > MAX_DOCUMENTS:
        raise AppError(f"You can keep up to {MAX_DOCUMENTS} documents. Remove one before adding more.",
                       code="too_many_documents", status_code=409,
                       details={"max_documents": MAX_DOCUMENTS, "current": len(existing)})

    max_bytes = c.settings.resume_max_bytes
    parsed: list[dict] = []
    for upload in uploads:
        filename = (upload.filename or "document")[-120:]
        data = await upload.read(max_bytes + 1)
        if len(data) > max_bytes:
            raise InvalidResume(f"{filename} is too large (maximum {max_bytes // (1024 * 1024)} MB per file).")
        if not data:
            raise InvalidResume(f"{filename} is empty.")
        try:
            kind = sniff_type(filename, data)
            text = extract_text(kind, data, max_chars=c.settings.resume_max_chars)
        except InvalidResume as exc:
            raise InvalidResume(f"{filename}: {exc.message}") from exc
        parsed.append({"filename": filename, "kind": kind, "text": text})
        del data

    now = utcnow()
    docs = []
    for p in parsed:
        doc = {"_id": new_id(), "user_id": user["_id"], "filename": p["filename"], "kind": p["kind"],
               "text": p["text"], "char_count": len(p["text"]), "created_at": now, "consent_at": now}
        await c.db.resumes.insert_one(doc)
        docs.append(doc)
    await c.db.users.update_one({"_id": user["_id"]}, {"$set": {"consents.resume_processing_at": now}})
    all_docs = await _documents(c, user["_id"])
    return {"uploaded": [public_document(d) for d in docs],
            "documents": [public_document(d) for d in all_docs], "max_documents": MAX_DOCUMENTS}


@router.get("")
async def list_documents(user: CurrentUser, c: ContainerDep) -> dict:
    return {"documents": [public_document(d) for d in await _documents(c, user["_id"])],
            "max_documents": MAX_DOCUMENTS}


@router.get("/current")
async def get_resume(user: CurrentUser, c: ContainerDep) -> dict:
    """Older clients: the first uploaded document."""
    docs = await _documents(c, user["_id"])
    if not docs:
        raise NotFound("No resume uploaded", code="resume_missing")
    return public_document(docs[0])


@router.delete("/{document_id}", status_code=status.HTTP_204_NO_CONTENT)
async def delete_document(document_id: str, user: CurrentUser, c: ContainerDep) -> None:
    if document_id == "current":  # older clients: delete everything
        await delete_all_documents(user, c)
        return
    result = await c.db.resumes.delete_one({"_id": document_id, "user_id": user["_id"]})
    if result.deleted_count == 0:
        raise NotFound("Document not found", code="document_not_found")
    await c.db.sessions.update_many({"user_id": user["_id"]}, {"$pull": {"resume_ids": document_id}})
    if not await c.db.resumes.count_documents({"user_id": user["_id"]}):
        await c.db.users.update_one({"_id": user["_id"]}, {"$set": {"consents.resume_processing_at": None}})


@router.delete("", status_code=status.HTTP_204_NO_CONTENT)
async def delete_all_documents(user: CurrentUser, c: ContainerDep) -> None:
    await c.db.resumes.delete_many({"user_id": user["_id"]})
    await c.db.sessions.update_many({"user_id": user["_id"]}, {"$set": {"resume_ids": []}})
    await c.db.users.update_one({"_id": user["_id"]}, {"$set": {"consents.resume_processing_at": None}})
