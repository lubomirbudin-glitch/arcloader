#!/usr/bin/env python3
"""Uploads the Mac build zip to the website's download storage (the Mac twin of windows/scripts/web-upload.ps1).

Storage: the public Tigris bucket on Fly.io (S3 API, https://fly.storage.tigris.dev); the website serves it as
https://mmmover.video/download/<file>. Credentials come from the environment only (never stored in the repo):
ARCLOADER_S3_KEY, ARCLOADER_S3_SECRET, bucket ARCLOADER_S3_BUCKET (default arcloader-dl) — the infrastructure names
stayed as they were since the rename to mmMover.

    python3 mac/scripts/web-upload.py --channel stable mac/build/mmMover-Mac-0.7.0.zip
        → https://mmmover.video/download/mmMover-Mac.zip       (dev: mmMover-Mac-dev.zip)
    python3 mac/scripts/web-upload.py --selftest      # AWS Signature V4 test vector, no network

Only the Mac channel zip is written; the Windows files and channels.json are never touched.
"""
import argparse, datetime, hashlib, hmac, http.client, os, sys, urllib.parse

HOST = "fly.storage.tigris.dev"
REGION = "auto"


def sigv4(method, path, headers, payload_hash, key, secret, region, now, query="", service="s3"):
    """AWS Signature Version 4: returns the Authorization header value (pure, no network)."""
    amz_date = now.strftime("%Y%m%dT%H%M%SZ")
    date = now.strftime("%Y%m%d")
    lower = {k.lower(): str(v).strip() for k, v in headers.items()}
    names = sorted(lower)
    canonical_headers = "".join(f"{n}:{lower[n]}\n" for n in names)
    signed = ";".join(names)
    canonical = f"{method}\n{path}\n{query}\n{canonical_headers}\n{signed}\n{payload_hash}"
    scope = f"{date}/{region}/{service}/aws4_request"
    to_sign = f"AWS4-HMAC-SHA256\n{amz_date}\n{scope}\n{hashlib.sha256(canonical.encode()).hexdigest()}"
    k = hmac.new(("AWS4" + secret).encode(), date.encode(), hashlib.sha256).digest()
    for part in (region, service, "aws4_request"):
        k = hmac.new(k, part.encode(), hashlib.sha256).digest()
    signature = hmac.new(k, to_sign.encode(), hashlib.sha256).hexdigest()
    return f"AWS4-HMAC-SHA256 Credential={key}/{scope}, SignedHeaders={signed}, Signature={signature}"


def selftest():
    # AWS S3 documentation example "GET Object" (Signature V4).
    now = datetime.datetime(2013, 5, 24, 0, 0, 0)
    payload = hashlib.sha256(b"").hexdigest()
    auth = sigv4("GET", "/test.txt", {"Host": "examplebucket.s3.amazonaws.com", "Range": "bytes=0-9",
                                      "x-amz-content-sha256": payload, "x-amz-date": "20130524T000000Z"},
                 payload, "AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", "us-east-1", now)
    expected = "f0e8bdb87c964420e857bd35b5d6ed310bd44f0170aba48dd91039c6036bdb41"
    ok = auth.endswith("Signature=" + expected)
    print("selftest", "OK" if ok else "FAILED: " + auth)
    return ok


def upload(zip_path, channel):
    key, secret = os.environ.get("ARCLOADER_S3_KEY"), os.environ.get("ARCLOADER_S3_SECRET")
    bucket = os.environ.get("ARCLOADER_S3_BUCKET") or "arcloader-dl"
    if not key or not secret:
        sys.exit("ARCLOADER_S3_KEY / ARCLOADER_S3_SECRET are not set — the website download storage cannot be written.")
    name = "mmMover-Mac-dev.zip" if channel == "dev" else "mmMover-Mac.zip"
    path = "/" + bucket + "/" + urllib.parse.quote(name)
    now = datetime.datetime.now(datetime.timezone.utc).replace(tzinfo=None)
    signed = {"host": HOST, "x-amz-content-sha256": "UNSIGNED-PAYLOAD", "x-amz-date": now.strftime("%Y%m%dT%H%M%SZ")}
    auth = sigv4("PUT", path, signed, "UNSIGNED-PAYLOAD", key, secret, REGION, now)
    size = os.path.getsize(zip_path)
    conn = http.client.HTTPSConnection(HOST, timeout=600)
    conn.putrequest("PUT", path, skip_host=True)
    for h, v in {**signed, "Authorization": auth, "Content-Type": "application/zip", "Content-Length": str(size),
                 "Cache-Control": "public, max-age=300"}.items():
        conn.putheader(h, v)
    conn.endheaders()
    with open(zip_path, "rb") as f:
        while chunk := f.read(1 << 20):
            conn.send(chunk)
    r = conn.getresponse()
    body = r.read().decode(errors="replace")
    if r.status != 200:
        sys.exit(f"Upload of {name} failed: HTTP {r.status} {body[:300]}")
    print(f"uploaded {name} ({size} bytes)")
    print(f"  storage: https://{bucket}.{HOST}/{name}")
    print(f"  website: https://mmmover.video/download/{name}")


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("zip", nargs="?")
    ap.add_argument("--channel", choices=["stable", "dev"], default="stable")
    ap.add_argument("--selftest", action="store_true")
    a = ap.parse_args()
    if a.selftest:
        sys.exit(0 if selftest() else 1)
    if not a.zip or not os.path.isfile(a.zip):
        sys.exit("give the build zip, e.g. mac/build/mmMover-Mac-0.7.0.zip")
    upload(a.zip, a.channel)
