"""Resume upload: strict validation, text extraction, and storage of extracted text only.

The original file is never written to disk or the database. Only the extracted text
(capped) is stored so the interviewer can tailor questions, and it can be deleted at any time.
"""

from __future__ import annotations

import io
import re
import zipfile

from app.core.errors import AppError

MAX_DOCUMENTS = 5  # resume, cover letter, LinkedIn export, portfolio, …

ALLOWED_EXTENSIONS = {".pdf": "application/pdf",
                      ".docx": "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                      ".txt": "text/plain"}


class InvalidResume(AppError):
    status_code = 422
    code = "invalid_resume"


def _extension(filename: str) -> str:
    m = re.search(r"(\.[A-Za-z0-9]{1,5})$", filename or "")
    return m.group(1).lower() if m else ""


def sniff_type(filename: str, data: bytes) -> str:
    """Determines the file type from its magic bytes and requires the extension to agree."""
    ext = _extension(filename)
    if ext not in ALLOWED_EXTENSIONS:
        raise InvalidResume("Unsupported file type. Upload a PDF, DOCX or TXT file.")
    if data.startswith(b"%PDF-"):
        actual = ".pdf"
    elif data.startswith(b"PK\x03\x04"):
        actual = ".docx"
    else:
        try:
            data.decode("utf-8")
            actual = ".txt"
        except UnicodeDecodeError as exc:
            raise InvalidResume("The file content is not a supported document.") from exc
    if actual != ext:
        raise InvalidResume("The file extension does not match its content.")
    return actual


def _clean(text: str, max_chars: int) -> str:
    text = text.replace("\x00", " ")
    text = re.sub(r"[ \t\r\f\v]+", " ", text)
    text = re.sub(r"\n\s*\n+", "\n\n", text).strip()
    return text[:max_chars]


def extract_text(kind: str, data: bytes, *, max_chars: int) -> str:
    if kind == ".txt":
        text = data.decode("utf-8", errors="replace")
    elif kind == ".pdf":
        from pypdf import PdfReader
        from pypdf.errors import PdfReadError

        try:
            reader = PdfReader(io.BytesIO(data))
            if reader.is_encrypted:
                raise InvalidResume("Password-protected PDFs are not supported.")
            if len(reader.pages) > 10:
                raise InvalidResume("Resume PDFs may have at most 10 pages.")
            text = "\n".join((page.extract_text() or "") for page in reader.pages)
        except PdfReadError as exc:
            raise InvalidResume("The PDF could not be read.") from exc
    else:
        _check_docx_archive(data)
        from docx import Document

        try:
            doc = Document(io.BytesIO(data))
        except Exception as exc:  # noqa: BLE001 - python-docx raises varied errors
            raise InvalidResume("The DOCX file could not be read.") from exc
        text = "\n".join(p.text for p in doc.paragraphs)
    text = _clean(text, max_chars)
    if len(text) < 100:
        raise InvalidResume("Not enough readable text was found. Scanned/image-only resumes are not supported.")
    return text


def _check_docx_archive(data: bytes, max_uncompressed: int = 20 * 1024 * 1024) -> None:
    """Guards against zip bombs and non-DOCX zip files."""
    try:
        with zipfile.ZipFile(io.BytesIO(data)) as zf:
            names = zf.namelist()
            if "word/document.xml" not in names:
                raise InvalidResume("The file is not a valid DOCX document.")
            if sum(i.file_size for i in zf.infolist()) > max_uncompressed or len(names) > 500:
                raise InvalidResume("The DOCX file is too large when uncompressed.")
    except zipfile.BadZipFile as exc:
        raise InvalidResume("The DOCX file is corrupted.") from exc


def combine_documents(docs: list[dict], max_chars: int) -> str:
    """Joins several documents for the interviewer prompt, sharing the character budget fairly."""
    docs = [d for d in docs if d.get("text")]
    if not docs:
        return ""
    budget = max(500, max_chars // len(docs))
    parts = []
    for i, d in enumerate(docs, 1):
        text = d["text"][:budget]
        parts.append(f"Document {i}: {d.get('filename', 'document')}\n{text}")
    return "\n\n".join(parts)[:max_chars]
