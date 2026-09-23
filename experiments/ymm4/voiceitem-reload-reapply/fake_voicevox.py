import argparse
import json
import os
import wave
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from io import BytesIO
from urllib.parse import urlparse, parse_qs

parser = argparse.ArgumentParser()
parser.add_argument("--port", type=int, required=True)
parser.add_argument("--output", required=True)
args = parser.parse_args()
os.makedirs(args.output, exist_ok=True)
log_path = os.path.join(args.output, "fake-server-requests.jsonl")

def log(obj):
    with open(log_path, "a", encoding="utf-8") as f:
        f.write(json.dumps(obj, ensure_ascii=False) + "\n")

def default_query():
    return {
        "accent_phrases": [{
            "moras": [{
                "text": "ア",
                "consonant": None,
                "consonant_length": None,
                "vowel": "a",
                "vowel_length": 0.12,
                "pitch": 5.0
            }],
            "accent": 1,
            "pause_mora": {
                "text": "、",
                "consonant": None,
                "consonant_length": None,
                "vowel": "pau",
                "vowel_length": 0.25,
                "pitch": 0.0
            },
            "is_interrogative": False
        }],
        "speedScale": 1.0,
        "pitchScale": 0.0,
        "intonationScale": 1.0,
        "volumeScale": 1.0,
        "prePhonemeLength": 0.1,
        "postPhonemeLength": 0.1,
        "outputSamplingRate": 24000,
        "outputStereo": False,
        "kana": "ア'"
    }

def wav_for_synthesis(raw):
    corrected = False
    try:
        body = json.loads(raw.decode("utf-8"))
        for phrase in body.get("accent_phrases", []):
            pause = phrase.get("pause_mora")
            if pause is not None and float(pause.get("vowel_length", -1)) == 0.0:
                corrected = True
                break
    except Exception:
        pass

    frames = 2700 if corrected else 2400
    sample = b"\x01\x00" if corrected else b"\x00\x00"
    bio = BytesIO()
    with wave.open(bio, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(24000)
        w.writeframes(sample * frames)
    return bio.getvalue(), corrected

class Handler(BaseHTTPRequestHandler):
    def _body(self):
        n = int(self.headers.get("Content-Length", "0"))
        return self.rfile.read(n) if n else b""

    def _json(self, value, code=200):
        data = json.dumps(value, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        parsed = urlparse(self.path)
        log({"method": "GET", "path": parsed.path, "query": parse_qs(parsed.query)})
        if parsed.path == "/version":
            self._json("0.0.0-cnwl")
        elif parsed.path == "/speakers":
            self._json([{
                "name": "CNWL Speaker",
                "speaker_uuid": "11111111-1111-1111-1111-111111111111",
                "styles": [{"name": "Normal", "id": 1, "type": "talk"}],
                "version": "0.0.0",
                "supported_features": {"permitted_synthesis_morphing": "SELF_ONLY"}
            }])
        elif parsed.path == "/is_initialized_speaker":
            self._json(True)
        elif parsed.path == "/engine_manifest":
            self._json({
                "manifest_version": "0.13.1",
                "name": "CNWL Fake",
                "brand_name": "CNWL",
                "uuid": "00000000-0000-0000-0000-000000000001",
                "version": "0.0.0",
                "url": "https://example.invalid",
                "command": "",
                "port": args.port,
                "icon": "",
                "default_sampling_rate": 24000,
                "frame_rate": 93.75,
                "terms_of_service": "",
                "update_infos": [],
                "dependency_licenses": [],
                "supported_features": {}
            })
        else:
            self._json({}, 200)

    def do_POST(self):
        parsed = urlparse(self.path)
        raw = self._body()
        text = raw.decode("utf-8", errors="replace")
        entry = {"method": "POST", "path": parsed.path, "query": parse_qs(parsed.query), "body": text}
        log(entry)

        if parsed.path == "/audio_query":
            self._json(default_query())
            return

        if parsed.path == "/accent_phrases":
            self._json(default_query()["accent_phrases"])
            return

        if parsed.path == "/initialize_speaker":
            self._json({})
            return

        if parsed.path == "/synthesis":
            wav, corrected = wav_for_synthesis(raw)
            log({"method": "OBSERVE", "path": "/synthesis-result", "corrected": corrected, "wav_length": len(wav)})
            self.send_response(200)
            self.send_header("Content-Type", "audio/wav")
            self.send_header("Content-Length", str(len(wav)))
            self.end_headers()
            self.wfile.write(wav)
            return

        self._json({})

    def log_message(self, format, *args):
        pass

server = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
with open(os.path.join(args.output, "fake-server-ready.txt"), "w", encoding="utf-8") as f:
    f.write(str(args.port))
server.serve_forever()
