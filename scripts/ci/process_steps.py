"""Run one logged CI step and stop only its owned process tree on timeout."""
import os
import signal
import subprocess
import time


def run(command, cwd, environment, log, steps, timeout=1200):
    started = time.monotonic()
    record = {"command": [str(value) for value in command], "log": str(log), "exitCode": None}
    steps.append(record)
    print(log.name, flush=True)
    with log.open("w", encoding="utf-8") as output:
        process = subprocess.Popen(record["command"], cwd=cwd, env=environment,
                                   stdout=output, stderr=subprocess.STDOUT,
                                   start_new_session=os.name != "nt",
                                   creationflags=subprocess.CREATE_NEW_PROCESS_GROUP if os.name == "nt" else 0)
        try:
            record["exitCode"] = process.wait(timeout=timeout)
        except (KeyboardInterrupt, subprocess.TimeoutExpired):
            if process.poll() is not None:
                pass
            elif os.name == "nt":
                subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                               stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, check=True, timeout=15)
            else:
                os.killpg(process.pid, signal.SIGKILL)
            process.wait(timeout=10)
            record["exitCode"] = process.returncode
            raise
        finally:
            record["seconds"] = time.monotonic() - started
    if record["exitCode"] != 0:
        raise RuntimeError(f"Step failed ({record['exitCode']}): {log}")
