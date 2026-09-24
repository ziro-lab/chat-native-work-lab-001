import argparse
import json
import os
import threading
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
lock = threading.Lock()

def log(obj):
    with lock:
        with open(log_path, "a", encoding="utf-8") as f:
            f.write(json.dumps(obj, ensure_ascii=False) + "\n")

def reading_for(text):
    return (text or "").replace("。", "")

def mora(text):
    return {
        "text": text,
        "consonant": None,
        "consonant_length": None,
        "vowel": "a",
        "vowel_length": 0.12,
        "pitch": 5.0,
    }

def pause(length=0.25):
    return {
        "text": "、",
        "consonant": None,
        "consonant_length": None,
        "vowel": "pau",
        "vowel_length": length,
        "pitch": 0.0,
    }

def phrase(chars, with_pause, index):
    moras = [mora(ch) for ch in chars]
    accent = 1 if not moras else min(len(moras), 1 + (index % min(3, len(moras))))
    return {
        "moras": moras,
        "accent": accent,
        "pause_mora": pause(0.25 + (0.05 * min(index, 2))) if with_pause else None,
        "is_interrogative": False,
    }

def phrases_for(text):
    reading = reading_for(text)
    if reading == "曖、昧境界":
        return [
            phrase(["曖"], True, 0),
            phrase(["、"], True, 1),
            phrase(["昧", "境", "界"], False, 2),
        ]

    parts = reading.split("、")
    result = []
    for i, part in enumerate(parts):
        result.append(phrase(list(part), i < len(parts) - 1, i))
    return result

def query_for(text):
    return {
        "accent_phrases": [],
        "speedScale": 1.0,
        "pitchScale": 0.0,
        "intonationScale": 1.0,
        "volumeScale": 1.0,
        "prePhonemeLength": 0.1,
        "postPhonemeLength": 0.1,
        "outputSamplingRate": 24000,
        "outputStereo": False,
        "kana": reading_for(text),
        "pauseLength": None,
        "pauseLengthScale": 1.0,
    }

def make_wav(frames, sample):
    bio = BytesIO()
    with wave.open(bio, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(24000)
        w.writeframes(sample * frames)
    return bio.getvalue()

def synthesis_shape(raw):
    zero_pauses = 0
    helper_zero = 0
    phrases = 0
    try:
        body = json.loads(raw.decode("utf-8"))
        for p in body.get("accent_phrases", []):
            phrases += 1
            pm = p.get("pause_mora")
            if pm is not None and float(pm.get("vowel_length", -1)) == 0.0:
                zero_pauses += 1
            for m in p.get("moras", []):
                if m.get("text") == "ヌ" and float(m.get("vowel_length", -1)) == 0.0:
                    helper_zero += 1
    except Exception:
        pass
    frames = 2400 + zero_pauses * 160 + helper_zero * 80
    sample = bytes([min(250, zero_pauses + helper_zero + 1), 0])
    return make_wav(frames, sample), {
        "phrase_count": phrases,
        "zero_pause_count": zero_pauses,
        "helper_zero_count": helper_zero,
    }

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
        query = parse_qs(parsed.query)
        log({"method": "GET", "path": parsed.path, "query": query})

        if parsed.path == "/version":
            self._json("0.0.0-vqa-forced-boundary")
        elif parsed.path == "/speakers":
            self._json([{
                "name": "CNWL VQA",
                "speaker_uuid": "11111111-1111-1111-1111-111111111111",
                "styles": [{"name": "Normal", "id": 1, "type": "talk"}],
                "version": "0.0.0",
                "supported_features": {"permitted_synthesis_morphing": "SELF_ONLY"},
            }])
        elif parsed.path == "/speaker_info":
            self._json({
                "policy": "",
                "portrait": "",
                "style_infos": [{
                    "id": 1,
                    "icon": "",
                    "portrait": None,
                    "voice_samples": ["", "", ""],
                }],
            })
        elif parsed.path == "/is_initialized_speaker":
            self._json(True)
        elif parsed.path == "/engine_manifest":
            self._json({
                "manifest_version": "0.13.1",
                "name": "CNWL VQA",
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
                "supported_features": {},
            })
        else:
            self._json({})

    def do_POST(self):
        parsed = urlparse(self.path)
        query = parse_qs(parsed.query)
        raw = self._body()
        text_body = raw.decode("utf-8", errors="replace")
        log({"method": "POST", "path": parsed.path, "query": query, "body": text_body})

        text = query.get("text", [""])[0]

        if parsed.path == "/audio_query":
            self._json(query_for(text))
            return

        if parsed.path == "/accent_phrases":
            self._json(phrases_for(text))
            return

        if parsed.path == "/initialize_speaker":
            self._json({})
            return

        if parsed.path == "/synthesis":
            wav, shape = synthesis_shape(raw)
            log({
                "method": "OBSERVE",
                "path": "/synthesis-result",
                **shape,
                "wav_length": len(wav),
            })
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
