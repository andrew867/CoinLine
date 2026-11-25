import io
import json
import tempfile
import unittest
import wave
from dataclasses import replace
from pathlib import Path

from coinline_audiogram_agi import Agi, Config, Disconnected, MessageSpool, ServiceError


class Media:
    def __init__(self, frames=8000, played=True):
        self.frames = frames
        self.played = played
        self.play_count = 0

    def record(self, stem, limit):
        with wave.open(str(stem.with_suffix(".wav")), "wb") as audio:
            audio.setparams((1, 2, 8000, 0, "NONE", "not compressed"))
            audio.writeframes(b"\x00\x00" * self.frames)

    def play(self, stem):
        self.play_count += 1
        if self.played is None:
            raise Disconnected("test hangup")
        return self.played


class LocalMessageTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        root = Path(self.directory.name)
        for name in ("staging", "outgoing", "archive"):
            (root / name).mkdir()
        self.config = Config(root / "data", root / "staging", root / "outgoing", root / "archive",
                             {"lab-recipient": "7005"}, True)

    def test_record_queue_and_deliver_once(self):
        spool = MessageSpool(self.config)
        media = Media()
        identifier = spool.record(media, "lab-recipient")
        files = list(self.config.outgoing.iterdir())
        self.assertEqual(len(files), 1)
        body = files[0].read_text(encoding="ascii")
        self.assertIn("Channel: Local/7005@coinline-audiogram-recipients/n\n", body)
        self.assertIn(f"Setvar: COINLINE_AUDIOGRAM_MESSAGE_ID={identifier}\n", body)
        self.assertIn("Archive: yes\n", body)
        self.assertIn("MaxRetries: 2\n", body)
        self.assertEqual(list(self.config.staging.iterdir()), [])
        self.assertEqual(spool.statuses()[0]["state"], "queued")
        self.assertFalse(spool.enqueue(identifier))
        self.assertTrue(spool.deliver(media, identifier))
        self.assertFalse(spool.deliver(media, identifier))
        self.assertEqual(media.play_count, 1)
        self.assertEqual(spool.statuses()[0]["state"], "delivered")

    def test_interrupted_playback_is_not_delivery(self):
        spool = MessageSpool(self.config)
        media = Media(played=None)
        identifier = spool.record(media, "lab-recipient")
        with self.assertRaises(Disconnected):
            spool.deliver(media, identifier)
        self.assertEqual(spool.statuses()[0]["state"], "incomplete")
        self.assertFalse(spool.deliver(media, identifier))

    def test_archive_does_not_prove_audio_delivery_and_prevents_requeue(self):
        spool = MessageSpool(self.config)
        identifier = spool.record(Media(), "lab-recipient")
        queued = next(self.config.outgoing.iterdir())
        archived = self.config.archive / queued.name
        queued.rename(archived)
        with archived.open("a", encoding="ascii") as file:
            file.write("Status: Completed\n")
        # Represent the recorded-to-queued bookkeeping not having committed.
        with spool.connect() as db:
            db.execute("UPDATE message SET state='recorded' WHERE id=?", (identifier,))
        self.assertTrue(spool.enqueue(identifier))
        self.assertEqual(list(self.config.outgoing.iterdir()), [])
        status = spool.statuses()[0]
        self.assertEqual(status["asterisk_status"], "Completed")
        self.assertEqual(status["state"], "queued")

    def test_unknown_recipient_disabled_service_and_short_recording_are_rejected(self):
        with self.assertRaises(ServiceError):
            MessageSpool(replace(self.config, enabled=False))
        with self.assertRaises(ServiceError):
            MessageSpool(replace(self.config, recipients={"lab-recipient": "7005@outside"}))
        spool = MessageSpool(self.config)
        with self.assertRaises(ServiceError):
            spool.record(Media(), "outside")
        with self.assertRaises(ServiceError):
            spool.record(Media(frames=200), "lab-recipient")
        with self.assertRaises(ServiceError):
            spool.deliver(Media(), "../outside")
        self.assertEqual(spool.statuses(), [])
        self.assertEqual(list(self.config.outgoing.iterdir()), [])

    def test_agi_commands_and_playback_confirmation(self):
        incoming = io.StringIO("agi_request: ignored\nagi_callerid: not-a-routing-value\n\n"
                               "200 result=0\n200 result=35 (dtmf) endpos=8000\n"
                               "200 result=0 endpos=8000\n200 result=1 (SUCCESS)\n")
        outgoing = io.StringIO()
        agi = Agi(incoming, outgoing)
        agi.read_environment()
        stem = self.config.data / "message"
        agi.record(stem, 60)
        self.assertTrue(agi.play(stem))
        commands = outgoing.getvalue().splitlines()
        self.assertEqual(commands[0], "ANSWER")
        self.assertIn('wav "#" 60000 0 BEEP s=5', commands[1])
        self.assertTrue(commands[2].startswith("STREAM FILE "))
        self.assertEqual(commands[3], "GET VARIABLE PLAYBACKSTATUS")
        self.assertNotIn("not-a-routing-value", outgoing.getvalue())

    def test_configuration_files_are_explicitly_opt_in(self):
        path = self.config.data.parent / "config.json"
        path.write_text(json.dumps({"data_directory": str(self.config.data),
            "callfile_staging_directory": str(self.config.staging), "callfile_outgoing_directory": str(self.config.outgoing),
            "callfile_archive_directory": str(self.config.archive), "recipients": {"lab-recipient": "7005"}}), encoding="utf-8")
        self.assertFalse(Config.load(path).enabled)


if __name__ == "__main__":
    unittest.main()
