"""Small LAN gateway for the Quest demo. Set keys in the PC environment, not Unity."""

import argparse
import json
import os
import re
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.error import HTTPError, URLError
from urllib.parse import urlsplit
from urllib.request import Request, urlopen


PROVIDERS = {
    "openrouter": ("OPENROUTER_API_KEY", "https://openrouter.ai/api/v1/chat/completions"),
    "grok": ("GROK_API_KEY", "https://api.x.ai/v1/chat/completions"),
}
MODEL_PATTERN = re.compile(r"^[A-Za-z0-9._:/-]{1,100}$")
VOICE_PATH = re.compile(r"^/v1/text-to-speech/[A-Za-z0-9_-]+/stream$")
MAX_BODY = 20 * 1024 * 1024


def build_ai_request(payload, environment):
    provider = payload.get("provider")
    model = payload.get("model", "")
    prompt = payload.get("prompt", "")
    if not isinstance(model, str) or not MODEL_PATTERN.fullmatch(model):
        raise ValueError("Invalid model name")
    if not isinstance(prompt, str) or not prompt.strip() or len(prompt) > 20000:
        raise ValueError("Invalid prompt")

    if provider == "google":
        key = environment.get("GEMINI_API_KEY")
        if not key:
            raise ValueError("GEMINI_API_KEY is not configured on the PC")
        url = f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent"
        body = {
            "contents": [{"parts": [{"text": prompt}]}],
            "generationConfig": {"responseMimeType": "application/json"},
        }
        headers = {"Content-Type": "application/json", "X-goog-api-key": key}
    elif provider in PROVIDERS:
        key_name, url = PROVIDERS[provider]
        key = environment.get(key_name)
        if not key:
            raise ValueError(f"{key_name} is not configured on the PC")
        body = {
            "model": model,
            "messages": [{"role": "user", "content": prompt}],
            "response_format": {"type": "json_object"},
        }
        headers = {"Content-Type": "application/json", "Authorization": f"Bearer {key}"}
    else:
        raise ValueError("Unsupported AI provider")

    return Request(url, json.dumps(body).encode(), headers, method="POST")


def parse_ai_response(provider, raw):
    data = json.loads(raw)
    if provider == "google":
        text = data["candidates"][0]["content"]["parts"][0]["text"]
    else:
        text = data["choices"][0]["message"]["content"]
    if not isinstance(text, str) or not text.strip():
        raise ValueError("AI provider returned an empty answer")
    return {"response": text}


def build_speech_request(path, body, content_type, environment):
    parsed = urlsplit(path)
    if parsed.path != "/v1/speech-to-text" and not VOICE_PATH.fullmatch(parsed.path):
        raise ValueError("Unsupported speech route")
    key = environment.get("ELEVENLABS_API_KEY")
    if not key:
        raise ValueError("ELEVENLABS_API_KEY is not configured on the PC")
    url = "https://api.elevenlabs.io" + parsed.path
    if parsed.query:
        url += "?" + parsed.query
    return Request(url, body, {"Content-Type": content_type, "Xi-api-key": key}, method="POST")


class Handler(BaseHTTPRequestHandler):
    def authorized(self):
        allowed = getattr(self.server, "allowed_clients", {"127.0.0.1", "::1"})
        if self.client_address[0] in allowed:
            return True
        self.respond(403, b'{"error":"Client is not allowed"}')
        return False

    def do_GET(self):
        if not self.authorized():
            return
        if self.path == "/health":
            self.respond(200, b'{"status":"ready"}')
        else:
            self.respond(404, b'{"error":"Not found"}')

    def do_POST(self):
        if not self.authorized():
            return
        try:
            length = int(self.headers.get("Content-Length", "0"))
            if length <= 0 or length > MAX_BODY:
                raise ValueError("Request body is empty or too large")
            body = self.rfile.read(length)
            if self.path == "/api/answer":
                payload = json.loads(body)
                if not isinstance(payload, dict):
                    raise ValueError("Answer request must be a JSON object")
                request = build_ai_request(payload, os.environ)
                with urlopen(request, timeout=45) as response:
                    try:
                        result = parse_ai_response(payload["provider"], response.read())
                    except (ValueError, KeyError, IndexError, TypeError):
                        self.respond(502, b'{"error":"Invalid AI provider response"}')
                        return
                self.respond(200, json.dumps(result).encode())
            else:
                request = build_speech_request(self.path, body, self.headers.get("Content-Type", ""), os.environ)
                with urlopen(request, timeout=60) as response:
                    self.respond(response.status, response.read(), response.headers.get("Content-Type", "application/octet-stream"))
        except (ValueError, KeyError, json.JSONDecodeError) as error:
            self.respond(400, json.dumps({"error": str(error)}).encode())
        except HTTPError as error:
            self.respond(error.code, error.read(), error.headers.get("Content-Type", "application/json"))
        except (URLError, TimeoutError):
            self.respond(502, b'{"error":"Upstream service unavailable"}')

    def respond(self, status, body, content_type="application/json"):
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="127.0.0.1", help="Use your PC LAN IP for Quest access")
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--allow-client", action="append", help="Quest IP allowed to use the PC's cloud credentials")
    options = parser.parse_args()
    if options.host not in ("127.0.0.1", "::1", "localhost") and not options.allow_client:
        parser.error("--allow-client is required when listening on a LAN address")
    server = ThreadingHTTPServer((options.host, options.port), Handler)
    server.allowed_clients = {"127.0.0.1", "::1", *(options.allow_client or [])}
    print(f"Assistant gateway listening on {options.host}:{options.port}", flush=True)
    server.serve_forever()
