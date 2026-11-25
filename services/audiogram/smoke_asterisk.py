"""Exercise the real AGI and Asterisk call-file spool using Local channels only."""

import json
import math
import shutil
import struct
import subprocess
import tempfile
import time
import wave
from pathlib import Path

from coinline_audiogram_agi import Config, MessageSpool


def main():
    asterisk = shutil.which("asterisk") or "/usr/sbin/asterisk"
    if not Path(asterisk).is_file():
        raise RuntimeError("Install Asterisk before running this standalone Local-channel test")
    modules = next((path for path in [Path("/usr/lib/asterisk/modules"), *Path("/usr/lib").glob("*/asterisk/modules")]
                    if (path / "res_agi.so").is_file()), None)
    documentation = Path("/usr/share/asterisk/documentation")
    if modules is None or not (documentation / "core-en_US.xml").is_file():
        raise RuntimeError("Asterisk distribution modules or core XML documentation are missing")
    service = Path(__file__).resolve().parent / "coinline_audiogram_agi.py"
    with tempfile.TemporaryDirectory(prefix="coinline-audio-smoke-") as tmp:
        root = Path(tmp)
        for directory in ["etc", "agi", "run", "log", "db", "data", "astdata/sounds", "spool/stage", "spool/outgoing", "spool/outgoing_done"]:
            (root / directory).mkdir(parents=True, exist_ok=True)
        # Asterisk's core configuration initialization requires its installed XML
        # definitions even when the PBX uses an otherwise isolated data directory.
        (root / "astdata/documentation").symlink_to(documentation, target_is_directory=True)
        agi = root / "agi" / service.name
        shutil.copyfile(service, agi)
        agi.chmod(0o700)
        config_path = root / "service.json"
        config_path.write_text(json.dumps({"enabled": True, "data_directory": str(root / "data"),
            "callfile_staging_directory": str(root / "spool/stage"), "callfile_outgoing_directory": str(root / "spool/outgoing"),
            "callfile_archive_directory": str(root / "spool/outgoing_done"), "recipients": {"lab-recipient": "7005"},
            "duration_limit": 8, "minimum_seconds": 1, "wait_seconds": 5, "retries": 0}), encoding="utf-8")
        source = root / "source"
        with wave.open(str(source.with_suffix(".wav")), "wb") as audio:
            audio.setparams((1, 2, 8000, 0, "NONE", "not compressed"))
            audio.writeframes(b"".join(struct.pack("<h", int(1800 * math.sin(2 * math.pi * 440 * i / 8000)))
                                       for i in range(4 * 8000)))
        with wave.open(str(root / "astdata/sounds/beep.wav"), "wb") as audio:
            audio.setparams((1, 2, 8000, 0, "NONE", "not compressed"))
            audio.writeframes(b"".join(struct.pack("<h", int(1800 * math.sin(2 * math.pi * 800 * i / 8000)))
                                       for i in range(800)))
        conf = root / "etc/asterisk.conf"
        conf.write_text(f"""[directories]
astetcdir => {root / 'etc'}
astmoddir => {modules}
astvarlibdir => {root / 'astdata'}
astdbdir => {root / 'db'}
astkeydir => {root / 'etc'}
astdatadir => {root / 'astdata'}
astagidir => {root / 'agi'}
astspooldir => {root / 'spool'}
astrundir => {root / 'run'}
astlogdir => {root / 'log'}
[options]
verbose = 3
""", encoding="ascii")
        (root / "etc/modules.conf").write_text("""[modules]
autoload=no
load=res_timing_timerfd.so
load=format_wav.so
load=res_speech.so
load=res_agi.so
load=pbx_config.so
load=pbx_spool.so
load=app_playback.so
""", encoding="ascii")
        (root / "etc/logger.conf").write_text("[logfiles]\nconsole=notice,warning,error,verbose\n", encoding="ascii")
        (root / "etc/extensions.conf").write_text(f"""[general]
static=yes
writeprotect=yes
[coinline-audiogram-record-test]
exten => 7004,1,Set(AGISIGHUP=no)
 same => n,AGI({agi.name},{config_path},record,lab-recipient)
 same => n,Hangup()
[coinline-audiogram-recipients]
exten => 7005,1,Answer()
 same => n,Wait(15)
 same => n,Hangup()
[coinline-audiogram-playback]
exten => s,1,Set(AGISIGHUP=no)
 same => n,AGI({agi.name},{config_path},deliver,${{COINLINE_AUDIOGRAM_MESSAGE_ID}})
 same => n,Hangup()
""", encoding="ascii")
        spool = MessageSpool(Config.load(config_path))
        log = root / "log/console.txt"
        with log.open("w", encoding="utf-8") as stream:
            process = subprocess.Popen([asterisk, "-f", "-C", str(conf)], stdout=stream, stderr=subprocess.STDOUT)
            cli_base = [asterisk, "-C", str(conf), "-rx"]
            try:
                for _ in range(50):
                    if process.poll() is not None:
                        raise RuntimeError("Isolated Asterisk stopped during startup")
                    result = subprocess.run(cli_base + ["core show version"], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, timeout=5)
                    if result.returncode == 0 and "Asterisk" in result.stdout:
                        break
                    time.sleep(0.2)
                else:
                    raise RuntimeError("Isolated Asterisk did not start")
                result = subprocess.run(cli_base + [f"channel originate Local/7004@coinline-audiogram-record-test/n application Playback {source}"],
                                        stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, timeout=10)
                if result.returncode != 0:
                    raise RuntimeError("Local-channel origination failed")
                for _ in range(120):
                    records = spool.statuses()
                    if len(records) == 1 and records[0]["state"] == "delivered" and records[0]["asterisk_status"] == "Completed":
                        if not 1 <= records[0]["seconds"] <= 9:
                            raise RuntimeError("Unexpected recorded duration")
                        with wave.open(str(spool.stem(records[0]["id"]).with_suffix(".wav")), "rb") as recorded:
                            if not any(recorded.readframes(recorded.getnframes())):
                                raise RuntimeError("No synthetic source audio was recorded")
                        print("Real Asterisk Local-channel record, queue and playback verified")
                        return
                    time.sleep(0.25)
                raise RuntimeError(f"Local fixture did not deliver one message: {spool.statuses()}")
            except Exception:
                stream.flush()
                print(log.read_text(encoding="utf-8", errors="replace")[-12000:])
                raise
            finally:
                if process.poll() is None:
                    subprocess.run(cli_base + ["core stop now"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=5)
                    try:
                        process.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        process.terminate()
                        process.wait(timeout=5)


if __name__ == "__main__":
    main()
