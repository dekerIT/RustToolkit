from __future__ import annotations

import json
import logging
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse


class AlarmHTTPServer:
    def __init__(self, trigger_callback, stop_callback, host: str = "127.0.0.1", port: int = 47800) -> None:
        self.trigger_callback = trigger_callback
        self.stop_callback = stop_callback
        self.host = host
        self.port = port
        self._server: ThreadingHTTPServer | None = None
        self._thread: threading.Thread | None = None
        outer = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, format: str, *args) -> None:
                logging.getLogger(__name__).info(format, *args)

            def do_POST(self) -> None:
                outer.handle_post(self)

        self.handler = Handler

    def start(self) -> None:
        if self._thread and self._thread.is_alive():
            return
        try:
            self._server = ThreadingHTTPServer((self.host, self.port), self.handler)
            self._server.daemon_threads = True
            self._thread = threading.Thread(
                target=self._server.serve_forever,
                name="RustToolkit-AlarmHTTP",
                daemon=True,
            )
            self._thread.start()
        except OSError as exc:
            logging.getLogger(__name__).error("Could not start alarm HTTP server: %s", exc)

    def stop(self) -> None:
        if self._server:
            self._server.shutdown()
            self._server.server_close()
            self._server = None

    def handle_post(self, handler: BaseHTTPRequestHandler) -> None:
        parsed = urlparse(handler.path)
        if parsed.path == "/alarm":
            length = int(handler.headers.get("Content-Length", "0"))
            if length:
                try:
                    handler.rfile.read(length)
                except OSError:
                    pass
            reason = parse_qs(parsed.query).get("reason", ["Manual HTTP alarm"])[0]
            self.trigger_callback(reason)
            response = {"status": "triggered", "reason": reason}
            self._reply(handler, 200, response)
        elif parsed.path == "/stop":
            self.stop_callback()
            self._reply(handler, 200, {"status": "stopped"})
        else:
            self._reply(handler, 404, {"error": "Not found"})

    @staticmethod
    def _reply(handler: BaseHTTPRequestHandler, status: int, body: dict) -> None:
        payload = json.dumps(body).encode("utf-8")
        handler.send_response(status)
        handler.send_header("Content-Type", "application/json")
        handler.send_header("Content-Length", str(len(payload)))
        handler.end_headers()
        handler.wfile.write(payload)
