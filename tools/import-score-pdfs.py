#!/usr/bin/env python3
"""Split printed-book PDFs into one PDF per piece and upload them to Abdera.

The school uses these books with the publishers' permission for internal use only, so the
PDFs never enter this (public) repository: the source files stay on the operator's disk and
the per-piece files go straight into the API's database, where only signed-in teachers and
admins can open them.

Page ranges and the printed-page -> PDF-page mapping come from
frontend/src/data/school-books.json. Requires `pypdf` (tool-only dependency):

    python3 -m venv .venv && .venv/bin/pip install pypdf

Local (API running with Auth__DevLogin__Enabled=true, no password):
    .venv/bin/python tools/import-score-pdfs.py --dev-login \\
        --book piyano-albumu="$HOME/Downloads/Piyano Albümü.pdf"

Production (asks for an admin e-mail and password; nothing is stored):
    .venv/bin/python tools/import-score-pdfs.py --api https://okul.example.com \\
        --book really-easy-50-popular=./50-popular-songs.pdf

--out DIR writes the split files locally instead of uploading, to check them first.
"""
import argparse
import getpass
import http.cookiejar
import io
import json
import sys
import uuid
from pathlib import Path
from urllib.error import HTTPError
from urllib.request import HTTPCookieProcessor, Request, build_opener

from pypdf import PdfReader, PdfWriter

CATALOG = Path(__file__).resolve().parents[1] / "frontend/src/data/school-books.json"
MAX_BYTES = 4 * 1024 * 1024  # ScoreFile.MaxBytes on the API (Vercel caps bodies at 4.5 MB)


def pdf_page(book, printed):
    offset = [s for s in book["pdfPageMap"] if s["fromPage"] <= printed][-1]["offset"]
    return printed + offset


def split_entry(reader, book, entry):
    writer = PdfWriter()
    for printed in range(entry["page"], entry["endPage"] + 1):
        writer.add_page(reader.pages[pdf_page(book, printed) - 1])
    # Pages are copied as-is: the scans are JBIG2 images and re-encoding them needs jbig2dec.
    buffer = io.BytesIO()
    writer.write(buffer)
    return buffer.getvalue(), entry["endPage"] - entry["page"] + 1


class Api:
    def __init__(self, base):
        self.base = base.rstrip("/")
        self.opener = build_opener(HTTPCookieProcessor(http.cookiejar.CookieJar()))

    def send(self, method, path, body=None, content_type="application/json"):
        request = Request(self.base + path, data=body, method=method, headers={"Content-Type": content_type})
        try:
            with self.opener.open(request, timeout=60) as response:
                return response.status, response.read()
        except HTTPError as error:
            return error.code, error.read()

    def login(self, dev_login):
        if dev_login:
            status, body = self.send("POST", "/api/dev/auth/login", json.dumps({"role": "Admin"}).encode())
        else:
            email = input("Yönetici e-postası: ").strip()
            password = getpass.getpass("Şifre: ")
            payload = {"email": email, "password": password, "expectedRole": "Admin"}
            status, body = self.send("POST", "/api/auth/login", json.dumps(payload).encode())
        if status != 200:
            sys.exit(f"Giriş başarısız ({status}): {body.decode(errors='replace')[:300]}")

    def upload(self, entry_id, content, page_count):
        boundary = uuid.uuid4().hex
        parts = [
            f'--{boundary}\r\nContent-Disposition: form-data; name="pageCount"\r\n\r\n{page_count}\r\n'.encode(),
            f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{entry_id}.pdf"\r\n'
            f"Content-Type: application/pdf\r\n\r\n".encode(),
            content,
            f"\r\n--{boundary}--\r\n".encode(),
        ]
        return self.send("PUT", f"/api/library/score-files/{entry_id}", b"".join(parts),
                         f"multipart/form-data; boundary={boundary}")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--book", action="append", required=True, metavar="BOOK_ID=PDF", help="repeatable")
    parser.add_argument("--api", default="http://localhost:8080")
    parser.add_argument("--dev-login", action="store_true", help="local only: passwordless admin session")
    parser.add_argument("--out", type=Path, help="write split files here instead of uploading")
    args = parser.parse_args()

    books = {book["id"]: book for book in json.loads(CATALOG.read_text())["books"]}
    api = None
    if not args.out:
        api = Api(args.api)
        api.login(args.dev_login)

    failures = 0
    for spec in args.book:
        book_id, _, path = spec.partition("=")
        if book_id not in books:
            sys.exit(f"Bilinmeyen kitap: {book_id}. Seçenekler: {', '.join(books)}")
        book = books[book_id]
        reader = PdfReader(path)
        print(f"{book['title']}: {len(book['entries'])} eser, kaynak {len(reader.pages)} sayfa")
        for entry in book["entries"]:
            content, pages = split_entry(reader, book, entry)
            label = f"  {entry['id']:<40} s.{entry['page']}-{entry['endPage']} ({len(content) // 1024} KB)"
            if len(content) > MAX_BYTES:
                failures += 1
                print(f"{label} ATLANDI: 4 MB sınırını aşıyor")
                continue
            if args.out:
                args.out.mkdir(parents=True, exist_ok=True)
                (args.out / f"{entry['id']}.pdf").write_bytes(content)
                print(label)
                continue
            status, body = api.upload(entry["id"], content, pages)
            if status != 200:
                failures += 1
                print(f"{label} HATA {status}: {body.decode(errors='replace')[:200]}")
            else:
                print(label)
    if failures:
        sys.exit(f"{failures} eser yüklenemedi.")


if __name__ == "__main__":
    main()
