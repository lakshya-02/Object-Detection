import json
import io
import os
import threading
import unittest
from http.server import ThreadingHTTPServer
from unittest.mock import patch
from urllib.error import HTTPError
from urllib.request import Request, urlopen

from assistant_gateway import Handler, build_ai_request, build_speech_request, parse_ai_response


class GatewayTests(unittest.TestCase):
    def test_google_request_uses_host_key(self):
        request = build_ai_request(
            {"provider": "google", "model": "gemini-2.5-flash-lite", "prompt": "What is visible?"},
            {"GEMINI_API_KEY": "host-secret"},
        )

        self.assertIn("models/gemini-2.5-flash-lite:generateContent", request.full_url)
        self.assertEqual("host-secret", request.get_header("X-goog-api-key"))
        self.assertIn("What is visible?", request.data.decode())

    def test_speech_request_replaces_client_key(self):
        request = build_speech_request(
            "/v1/speech-to-text", b"audio", "multipart/form-data; boundary=test",
            {"ELEVENLABS_API_KEY": "host-speech-secret"},
        )

        self.assertEqual("https://api.elevenlabs.io/v1/speech-to-text", request.full_url)
        self.assertEqual("host-speech-secret", request.get_header("Xi-api-key"))
        self.assertEqual(b"audio", request.data)

    def test_google_response_normalizes_to_existing_envelope(self):
        raw = json.dumps({"candidates": [{"content": {"parts": [{"text": "{\"speak\":\"Hello\"}"}]}}]}).encode()

        self.assertEqual({"response": '{"speak":"Hello"}'}, parse_ai_response("google", raw))

    def test_http_answer_route_forwards_and_returns_normalized_response(self):
        server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        thread = threading.Thread(target=server.serve_forever)
        thread.start()
        try:
            upstream = io.BytesIO(json.dumps({
                "candidates": [{"content": {"parts": [{"text": '{"actions":[]}' }]}}]
            }).encode())
            with patch.dict(os.environ, {"GEMINI_API_KEY": "host-secret"}), patch(
                "assistant_gateway.urlopen", return_value=upstream
            ) as forward:
                payload = {"provider": "google", "model": "gemini-2.5-flash-lite", "prompt": "Organize books"}
                request = Request(
                    f"http://127.0.0.1:{server.server_port}/api/answer",
                    json.dumps(payload).encode(),
                    {"Content-Type": "application/json"},
                    method="POST",
                )
                with urlopen(request) as response:
                    self.assertEqual({"response": '{"actions":[]}'}, json.load(response))
                self.assertEqual("host-secret", forward.call_args.args[0].get_header("X-goog-api-key"))
        finally:
            server.shutdown()
            thread.join()
            server.server_close()

    def test_malformed_upstream_response_returns_gateway_error(self):
        server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        thread = threading.Thread(target=server.serve_forever)
        thread.start()
        try:
            with patch.dict(os.environ, {"GEMINI_API_KEY": "host-secret"}), patch(
                "assistant_gateway.urlopen", return_value=io.BytesIO(b'{"candidates":[]}')
            ):
                request = Request(
                    f"http://127.0.0.1:{server.server_port}/api/answer",
                    json.dumps({"provider": "google", "model": "gemini-2.5-flash-lite", "prompt": "Hello"}).encode(),
                    method="POST",
                )
                with self.assertRaises(HTTPError) as error:
                    urlopen(request)
                self.assertEqual(502, error.exception.code)
        finally:
            server.shutdown()
            thread.join()
            server.server_close()

    def test_client_outside_allowlist_is_rejected(self):
        server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        server.allowed_clients = {"10.0.0.2"}
        thread = threading.Thread(target=server.serve_forever)
        thread.start()
        try:
            with self.assertRaises(HTTPError) as error:
                urlopen(f"http://127.0.0.1:{server.server_port}/health")
            self.assertEqual(403, error.exception.code)
        finally:
            server.shutdown()
            thread.join()
            server.server_close()


if __name__ == "__main__":
    unittest.main()
