#!/usr/bin/env python3
"""Local Asterisk voice-message recording and delivery, separate from terminal authorization."""

import argparse
import json
import os
import re
import sqlite3
import sys
import tempfile
import uuid
import wave
from contextlib import contextmanager
from dataclasses import dataclass
from pathlib import Path


class ServiceError(Exception):
    pass


class Disconnected(ServiceError):
    pass


def quote(value: str) -> str:
    if any(ord(char) < 32 or ord(char) == 127 for char in value):
        raise ServiceError("Invalid AGI argument")
    return '"' + value.replace('\\', '\\\\').replace('"', '\\"') + '"'


class Agi:
    def __init__(self, incoming=sys.stdin, outgoing=sys.stdout):
        self.incoming = incoming
        self.outgoing = outgoing

    def read_environment(self):
        # The environment is not an authorization source. In particular, caller ID,
        # dialed digits and card information are never used as route parameters.
        for _ in range(128):
            line = self.incoming.readline(4097)
            if not line:
                raise Disconnected("AGI disconnected")
            if len(line) > 4096:
                raise ServiceError("Invalid AGI environment")
            if line.strip() == "":
                return
        raise ServiceError("Invalid AGI environment")

    def command(self, command: str) -> tuple[int, str | None]:
        self.outgoing.write(command + "\n")
        self.outgoing.flush()
        line = self.incoming.readline(4097)
        if not line or line.strip() == "HANGUP":
            raise Disconnected("AGI disconnected")
        match = re.fullmatch(r"200 result=(-?\d+)(?: \(([^\r\n]*)\))?(?: [^\r\n]*)?", line.rstrip("\r\n"))
        if not match or len(line) > 4096:
            raise ServiceError("Asterisk rejected AGI command")
        return int(match.group(1)), match.group(2)

    def record(self, stem: Path, seconds: int) -> None:
        result, _ = self.command("ANSWER")
        if result != 0:
            raise ServiceError("Could not answer recording call")
        try:
            self.command(f'RECORD FILE {quote(str(stem))} wav "#" {seconds * 1000} 0 BEEP s=5')
        except Disconnected:
            # A caller may finish by hanging up. The spool validates the saved WAV.
            pass

    def play(self, stem: Path) -> bool:
        result, _ = self.command(f'STREAM FILE {quote(str(stem))} "" 0')
        if result != 0:
            return False
        result, value = self.command("GET VARIABLE PLAYBACKSTATUS")
        return result == 1 and value == "SUCCESS"


@dataclass(frozen=True)
class Config:
    data: Path
    staging: Path
    outgoing: Path
    archive: Path
    recipients: dict[str, str]
    enabled: bool
    duration_limit: int = 60
    minimum_seconds: int = 1
    wait_seconds: int = 30
    retries: int = 2
    retry_seconds: int = 60

    @classmethod
    def load(cls, path: Path):
        data = json.loads(path.read_text(encoding="utf-8"))
        config = cls(Path(data["data_directory"]), Path(data["callfile_staging_directory"]),
                     Path(data["callfile_outgoing_directory"]), Path(data["callfile_archive_directory"]),
                     data["recipients"], data.get("enabled") is True,
                     data.get("duration_limit", 60), data.get("minimum_seconds", 1),
                     data.get("wait_seconds", 30), data.get("retries", 2), data.get("retry_seconds", 60))
        config.validate()
        return config

    def validate(self):
        paths = [self.data, self.staging, self.outgoing, self.archive]
        if not all(path.is_absolute() for path in paths) or len(set(path.resolve() for path in paths)) != 4:
            raise ServiceError("Use distinct absolute spool directories")
        for path in paths:
            quote(str(path))
        if not self.recipients or any(not re.fullmatch(r"[a-z][a-z0-9-]{0,31}", key)
                                     or not re.fullmatch(r"[0-9]{3,6}", extension)
                                     for key, extension in self.recipients.items()):
            raise ServiceError("Configure named local recipient extensions only")
        for value, low, high in [(self.duration_limit, 1, 300), (self.minimum_seconds, 1, self.duration_limit),
                                 (self.wait_seconds, 5, 120), (self.retries, 0, 5), (self.retry_seconds, 10, 3600)]:
            if type(value) is not int or not low <= value <= high:
                raise ServiceError("Invalid recording or delivery limit")


class MessageSpool:
    def __init__(self, config: Config):
        config.validate()
        self.config = config
        if not config.enabled:
            raise ServiceError("Local voice-message service is disabled")
        self.recordings = config.data / "recordings"
        self.recordings.mkdir(parents=True, mode=0o700, exist_ok=True)
        if not all(path.is_dir() for path in [config.staging, config.outgoing, config.archive]):
            raise ServiceError("Asterisk call-file directories must already exist")
        if config.staging.stat().st_dev != config.outgoing.stat().st_dev:
            raise ServiceError("Call-file staging and outgoing must share a filesystem")
        self.db_path = config.data / "messages.sqlite3"
        with self.connect() as db:
            db.execute("""CREATE TABLE IF NOT EXISTS message (
                id TEXT PRIMARY KEY, recipient TEXT NOT NULL, extension TEXT NOT NULL,
                seconds REAL NOT NULL, state TEXT NOT NULL, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP)""")

    @contextmanager
    def connect(self):
        db = sqlite3.connect(self.db_path, timeout=10)
        db.row_factory = sqlite3.Row
        try:
            with db:
                yield db
        finally:
            db.close()

    def stem(self, message_id: str) -> Path:
        if not re.fullmatch(r"[0-9a-f]{32}", message_id):
            raise ServiceError("Invalid message identifier")
        return self.recordings / message_id

    def record(self, agi: Agi, recipient: str) -> str:
        extension = self.config.recipients.get(recipient)
        if extension is None:
            raise ServiceError("Unknown local recipient")
        message_id = uuid.uuid4().hex
        stem = self.stem(message_id)
        agi.record(stem, self.config.duration_limit)
        audio = stem.with_suffix(".wav")
        try:
            with wave.open(str(audio), "rb") as file:
                rate = file.getframerate()
                frames = file.getnframes()
                if rate not in (8000, 16000) or file.getnchannels() != 1 or file.getsampwidth() != 2:
                    raise ServiceError("Unsupported recording format")
                seconds = frames / rate
                if not self.config.minimum_seconds <= seconds <= self.config.duration_limit + 1:
                    raise ServiceError("Recording duration outside configured bounds")
                if len(file.readframes(frames)) != frames * 2:
                    raise ServiceError("Incomplete recording")
        except (OSError, EOFError, wave.Error) as exc:
            raise ServiceError("No valid recorded message") from exc
        with self.connect() as db:
            db.execute("INSERT INTO message (id,recipient,extension,seconds,state) VALUES (?,?,?,?, 'recorded')",
                       (message_id, recipient, extension, seconds))
        self.enqueue(message_id)
        return message_id

    def enqueue(self, message_id: str) -> bool:
        stem = self.stem(message_id)
        filename = f"coinline-audiogram-{message_id}.call"
        destination = self.config.outgoing / filename
        archived = self.config.archive / filename
        with self.connect() as db:
            db.execute("BEGIN IMMEDIATE")
            row = db.execute("SELECT * FROM message WHERE id=?", (message_id,)).fetchone()
            if row is None:
                raise ServiceError("Unknown message")
            if row["state"] != "recorded":
                return False
            if not stem.with_suffix(".wav").is_file():
                raise ServiceError("Recorded message is missing")
            extension = self.config.recipients.get(row["recipient"])
            if extension != row["extension"]:
                raise ServiceError("Local recipient configuration changed")
            # Asterisk consumes only an atomically placed call file. The stable name
            # and archived copy also allow recovery if the DB commit was interrupted.
            if not destination.exists() and not archived.exists():
                body = (f"Channel: Local/{extension}@coinline-audiogram-recipients/n\n"
                        f"WaitTime: {self.config.wait_seconds}\nMaxRetries: {self.config.retries}\n"
                        f"RetryTime: {self.config.retry_seconds}\nContext: coinline-audiogram-playback\n"
                        f"Extension: s\nPriority: 1\nSetvar: COINLINE_AUDIOGRAM_MESSAGE_ID={message_id}\nArchive: yes\n")
                fd, temp = tempfile.mkstemp(prefix="coinline-audiogram-", suffix=".tmp", dir=self.config.staging)
                staged = Path(temp)
                try:
                    with os.fdopen(fd, "w", encoding="ascii", newline="\n") as file:
                        file.write(body)
                        file.flush()
                        os.fsync(file.fileno())
                    os.replace(staged, destination)
                finally:
                    staged.unlink(missing_ok=True)
            db.execute("UPDATE message SET state='queued',updated_at=CURRENT_TIMESTAMP WHERE id=?", (message_id,))
            return True

    def deliver(self, agi: Agi, message_id: str) -> bool:
        stem = self.stem(message_id)
        # Claim before playing. An answered call is not proof of completed delivery.
        with self.connect() as db:
            result = db.execute("UPDATE message SET state='playing',updated_at=CURRENT_TIMESTAMP "
                                "WHERE id=? AND state='queued'", (message_id,))
            if result.rowcount != 1:
                return False
        delivered = False
        try:
            if stem.with_suffix(".wav").is_file():
                delivered = agi.play(stem)
            return delivered
        finally:
            with self.connect() as db:
                db.execute("UPDATE message SET state=?,updated_at=CURRENT_TIMESTAMP WHERE id=? AND state='playing'",
                           ("delivered" if delivered else "incomplete", message_id))

    def statuses(self):
        with self.connect() as db:
            rows = [dict(row) for row in db.execute("SELECT id,recipient,seconds,state,created_at,updated_at FROM message ORDER BY created_at,id")]
        for row in rows:
            archived = self.config.archive / f"coinline-audiogram-{row['id']}.call"
            row["asterisk_status"] = None
            if archived.is_file():
                for line in archived.read_text(encoding="ascii", errors="replace").splitlines():
                    if line in ("Status: Expired", "Status: Completed", "Status: Failed"):
                        row["asterisk_status"] = line.removeprefix("Status: ")
        return rows


def main(argv=None) -> int:
    os.umask(0o077)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("config", type=Path)
    parser.add_argument("action", choices=["record", "deliver", "enqueue", "status"])
    parser.add_argument("argument", nargs="?")
    args = parser.parse_args(argv)
    try:
        spool = MessageSpool(Config.load(args.config))
        if args.action == "status":
            print(json.dumps(spool.statuses(), indent=2))
        elif args.action == "enqueue":
            if not args.argument:
                raise ServiceError("Missing message identifier")
            spool.enqueue(args.argument)
        else:
            if not args.argument:
                raise ServiceError("Missing local recipient or message identifier")
            agi = Agi()
            agi.read_environment()
            if args.action == "record":
                spool.record(agi, args.argument)
            elif not spool.deliver(agi, args.argument):
                return 1
        return 0
    except (ServiceError, OSError, sqlite3.Error, ValueError, KeyError, TypeError):
        # stdout carries AGI only. Do not log incoming identifiers, channel data,
        # audio, filenames supplied by callers or configuration values.
        print("CoinLine local audio-message operation did not complete.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
