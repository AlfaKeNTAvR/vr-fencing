# /// script
# requires-python = ">=3.10"
# dependencies = ["matplotlib>=3.8"]
# ///
"""Blade-contact logs from the headset (PER-136): list, pull the latest, summarize, plot.

While the Fencer recorder records (off-hand thumbstick, red REC), the app writes
blade-<UTC>.csv next to fencer-<UTC>.fencer on the Quest; the off-hand grip
writes a "felt wrong" mark into it. This script:

  uv run scripts/blade-log.py                 pull the newest blade log, summarize it
  uv run scripts/blade-log.py --plot          ... and draw PNG plots next to it
  uv run scripts/blade-log.py list            list the recordings on the headset
  uv run scripts/blade-log.py pull NAME       pull one (blade-...csv), summarize it
  uv run scripts/blade-log.py show FILE       summarize a log already on the PC

Pulled logs go to Recordings/ (gitignored). Each mark also gets its 1 s of data
around it as Recordings/<stem>-mark<N>.csv, all columns.

The headset must be on USB (adb -d) and authorized. ADB overrides the adb path.
"""

from __future__ import annotations

import argparse
import csv
import os
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

DEFAULT_ADB = "C:/Program Files/Unity/Hub/Editor/6000.0.85f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"
DEVICE_FOLDER = "/sdcard/Android/data/com.alfakentavr.vrfencing/files/recordings/"
REPO_ROOT = Path(__file__).resolve().parent.parent
PULL_FOLDER = REPO_ROOT / "Recordings"

# The window kept around each mark, seconds either side (1 s in all).
MARK_HALF_WINDOW_S = 0.5
# Rows printed per mark window: every Nth row, plus every row with an event.
MARK_PRINT_EVERY = 4
# A frame longer than this is a hitch (72 Hz is 13.9 ms).
HITCH_S = 0.020
# A drawn blade this far into a bell, a grip or the other blade counts as through it.
THROUGH_M = -0.002

MARK_COLUMNS = [
    "time_s",
    "in_contact",
    "press_n",
    "overlap_m",
    "contact_on_fencer_frac",
    "contact_on_dummy_frac",
    "fencer_grip_turn_deg",
    "fencer_bow_m",
    "dummy_grip_turn_deg",
    "dummy_bow_m",
    "fencer_slide_phase",
    "dummy_slide_phase",
    "saturated",
    "drawn_gap_m",
    "fencer_bell_gap_m",
    "dummy_bell_gap_m",
    "fencer_handle_gap_m",
    "dummy_handle_gap_m",
    "dummy_deflection_phase",
    "dummy_action",
    "sword_hand_speed_mps",
    "events",
]


@dataclass
class BladeLog:
    path: Path
    comments: list[str]
    columns: list[str]
    rows: list[dict[str, str]]


def main() -> int:
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    parser.add_argument(
        "command",
        nargs="?",
        default="latest",
        choices=["latest", "list", "pull", "show"],
    )
    parser.add_argument(
        "name",
        nargs="?",
        help="pull: the file name on the headset; show: a local CSV path",
    )
    parser.add_argument(
        "--plot", action="store_true", help="also write PNG plots next to the log"
    )
    parser.add_argument(
        "--with-fencer",
        action="store_true",
        help="also pull the matching fencer-<UTC>.fencer",
    )
    args = parser.parse_args()

    if args.command == "list":
        print(adb("shell", "ls", "-l", DEVICE_FOLDER))
        return 0

    if args.command == "show":
        if not args.name:
            parser.error("show needs a CSV path")
        path = Path(args.name)
    else:
        name = args.name if args.command == "pull" else find_latest_name()
        if not name:
            parser.error("pull needs a file name (see 'list')")
        path = pull(name)
        if args.with_fencer:
            pull(
                "fencer-" + name.removeprefix("blade-").removesuffix(".csv") + ".fencer"
            )

    log = read_log(path)
    print_summary(log)
    write_mark_windows(log)
    if args.plot:
        plot(log)
    return 0


def adb(*arguments: str) -> str:
    """Runs adb against the USB headset (-d) and returns its output."""
    executable = os.environ.get("ADB", DEFAULT_ADB)
    result = subprocess.run(
        [executable, "-d", *arguments], capture_output=True, text=True
    )
    if result.returncode != 0:
        sys.exit(
            f"adb {' '.join(arguments)} failed: {result.stderr.strip() or result.stdout.strip()}"
        )
    return result.stdout


def find_latest_name() -> str:
    names = sorted(
        line.strip()
        for line in adb("shell", "ls", DEVICE_FOLDER).splitlines()
        if line.strip().startswith("blade-")
    )
    if not names:
        sys.exit(
            f"No blade-*.csv on the headset in {DEVICE_FOLDER}: record with REC on first."
        )
    return names[-1]


def pull(name: str) -> Path:
    PULL_FOLDER.mkdir(exist_ok=True)
    target = PULL_FOLDER / name
    adb("pull", DEVICE_FOLDER + name, str(target))
    print(f"Pulled {name} to {target}")
    return target


def read_log(path: Path) -> BladeLog:
    with path.open(newline="", encoding="utf-8") as file:
        lines = file.read().splitlines()
    comments = [line for line in lines if line.startswith("#")]
    body = [line for line in lines if line and not line.startswith("#")]
    if not body:
        sys.exit(f"{path} has no column line: not a blade log, or empty.")
    reader = csv.DictReader(body)
    # A log cut short by a killed app may end in half a row: drop it.
    rows = [
        row
        for row in reader
        if None not in row.values() and len(row) == len(reader.fieldnames)
    ]
    return BladeLog(path, comments, list(reader.fieldnames), rows)


def number(row: dict[str, str], column: str) -> float | None:
    cell = row.get(column, "")
    return float(cell) if cell else None


def events_of(row: dict[str, str]) -> list[str]:
    return [event for event in row.get("events", "").split("|") if event]


def find_most(
    log: BladeLog, column: str, lowest: bool = False
) -> tuple[float | None, float | None]:
    """The largest (or lowest) value of a column and its time."""
    best = None
    best_time = None
    for row in log.rows:
        value = number(row, column)
        if value is None:
            continue
        if best is None or (value < best if lowest else value > best):
            best = value
            best_time = number(row, "time_s")
    return best, best_time


def describe(
    value: float | None,
    time_s: float | None,
    scale: float,
    unit: str,
    decimals: int = 1,
) -> str:
    if value is None:
        return "n/a"
    return f"{value * scale:.{decimals}f} {unit} at {time_s:.2f} s"


def print_summary(log: BladeLog) -> None:
    rows = log.rows
    print()
    for comment in log.comments[:2]:
        print(comment)
    if not rows:
        print("No rows.")
        return

    times = [number(row, "time_s") or 0.0 for row in rows]
    steps = [number(row, "dt_s") or 0.0 for row in rows[1:]]
    duration = times[-1] - times[0]
    contact_s = sum(
        number(row, "dt_s") or 0.0 for row in rows if row["in_contact"] == "1"
    )
    contacts = sum(
        1
        for before, row in zip(rows, rows[1:])
        if row["in_contact"] == "1" and before["in_contact"] != "1"
    )
    armed_s = sum(
        number(row, "dt_s") or 0.0 for row in rows if row["dummy_armed"] == "1"
    )
    counts: dict[str, int] = {}
    for row in rows:
        for event in events_of(row):
            counts[event] = counts.get(event, 0) + 1

    print(f"File            {log.path}")
    print(
        f"Duration        {duration:.1f} s, {len(rows)} rows, {len(rows) / max(duration, 1e-6):.1f} rows/s, "
        f"{sum(1 for step in steps if step > HITCH_S)} frames over {HITCH_S * 1000:.0f} ms"
    )
    print(f"Dummy armed     {armed_s:.1f} s")
    print(f"Contact time    {contact_s:.1f} s in {contacts} contacts")
    print(f"Slide-offs      {counts.get('slide_off_start', 0)}")
    print(f"Max press       {describe(*find_most(log, 'press_n'), 1.0, 'N', 2)}")
    print(f"Max overlap     {describe(*find_most(log, 'overlap_m'), 100.0, 'cm')}")
    print(
        f"Max bow         Fencer {describe(*find_most(log, 'fencer_bow_m'), 100.0, 'cm')}, Dummy {describe(*find_most(log, 'dummy_bow_m'), 100.0, 'cm')}"
    )
    print(
        f"Max grip turn   Fencer {describe(*find_most(log, 'fencer_grip_turn_deg'), 1.0, 'deg')}, Dummy {describe(*find_most(log, 'dummy_grip_turn_deg'), 1.0, 'deg')}"
    )
    print(
        f"Max hand give   {describe(*find_most_vector(log, 'dummy_hand_give'), 100.0, 'cm')}"
    )
    print(
        f"Max sword speed {describe(*find_most(log, 'sword_hand_speed_mps'), 1.0, 'm/s', 2)}"
    )
    for column, label in (
        ("drawn_gap_m", "Drawn blades"),
        ("fencer_bell_gap_m", "Fencer's bell"),
        ("dummy_bell_gap_m", "Dummy's bell"),
        ("fencer_handle_gap_m", "Fencer's grip"),
        ("dummy_handle_gap_m", "Dummy's grip"),
    ):
        lowest, at = find_most(log, column, lowest=True)
        through = sum(1 for row in rows if (number(row, column) or 0.0) < THROUGH_M)
        print(
            f"{label:<15} closest {describe(lowest, at, 100.0, 'cm')}, {through} frames through (< {THROUGH_M * 1000:.0f} mm)"
        )
    print(
        "Events          "
        + (
            ", ".join(f"{name} {count}" for name, count in sorted(counts.items()))
            or "none"
        )
    )

    marks = [row for row in rows if "mark" in events_of(row)]
    print(
        f"Marks           {len(marks)}"
        + (
            ": "
            + ", ".join(
                f"#{index + 1} at {number(row, 'time_s'):.2f} s"
                for index, row in enumerate(marks)
            )
            if marks
            else ""
        )
    )
    for index, mark in enumerate(marks):
        print_mark_window(log, index, number(mark, "time_s") or 0.0)


def find_most_vector(log: BladeLog, prefix: str) -> tuple[float | None, float | None]:
    best = None
    best_time = None
    for row in log.rows:
        parts = [number(row, f"{prefix}_{axis}_m") for axis in "xyz"]
        if None in parts:
            continue
        length = sum(part * part for part in parts) ** 0.5
        if best is None or length > best:
            best = length
            best_time = number(row, "time_s")
    return best, best_time


def window(log: BladeLog, time_s: float) -> list[dict[str, str]]:
    return [
        row
        for row in log.rows
        if abs((number(row, "time_s") or 0.0) - time_s) <= MARK_HALF_WINDOW_S
    ]


def print_mark_window(log: BladeLog, index: int, time_s: float) -> None:
    print()
    print(
        f"Mark {index + 1} at {time_s:.2f} s: {MARK_HALF_WINDOW_S:.1f} s either side, every {MARK_PRINT_EVERY}th row and every event"
    )
    columns = [column for column in MARK_COLUMNS if column in log.columns]
    short = [
        column.replace("_frac", "")
        .replace("contact_on_", "on_")
        .replace("fencer_", "f_")
        .replace("dummy_", "d_")
        for column in columns
    ]
    print("  ".join(short))
    for row_index, row in enumerate(window(log, time_s)):
        if row_index % MARK_PRINT_EVERY == 0 or events_of(row):
            print("  ".join(row[column] or "-" for column in columns))


def write_mark_windows(log: BladeLog) -> None:
    marks = [
        number(row, "time_s") or 0.0 for row in log.rows if "mark" in events_of(row)
    ]
    for index, time_s in enumerate(marks):
        target = log.path.with_name(f"{log.path.stem}-mark{index + 1}.csv")
        with target.open("w", newline="", encoding="utf-8") as file:
            writer = csv.DictWriter(file, fieldnames=log.columns, lineterminator="\n")
            writer.writeheader()
            writer.writerows(window(log, time_s))
        print(f"Mark {index + 1}'s window: {target}")


def plot(log: BladeLog) -> None:
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    times = [number(row, "time_s") or 0.0 for row in log.rows]

    def series(column: str, scale: float = 1.0) -> list[float]:
        return [
            (number(row, column) if number(row, column) is not None else float("nan"))
            * scale
            for row in log.rows
        ]

    panels = [
        ("Press", "N", [("press_n", "press force", 1.0)]),
        (
            "Bow",
            "cm",
            [("fencer_bow_m", "Fencer", 100.0), ("dummy_bow_m", "Dummy", 100.0)],
        ),
        (
            "Grip turn",
            "deg",
            [
                ("fencer_grip_turn_deg", "Fencer", 1.0),
                ("dummy_grip_turn_deg", "Dummy", 1.0),
            ],
        ),
        (
            "Clearance",
            "cm",
            [
                ("drawn_gap_m", "drawn blades", 100.0),
                ("fencer_bell_gap_m", "Fencer's bell", 100.0),
                ("dummy_bell_gap_m", "Dummy's bell", 100.0),
                ("fencer_handle_gap_m", "Fencer's grip", 100.0),
                ("dummy_handle_gap_m", "Dummy's grip", 100.0),
            ],
        ),
        ("Sword hand", "m/s", [("sword_hand_speed_mps", "speed", 1.0)]),
    ]
    figure, axes = plt.subplots(
        len(panels),
        1,
        sharex=True,
        figsize=(14, 2.2 * len(panels)),
        constrained_layout=True,
    )
    marks = [number(row, "time_s") for row in log.rows if "mark" in events_of(row)]
    slides = [
        number(row, "time_s") for row in log.rows if "slide_off_start" in events_of(row)
    ]
    for axis, (title, unit, lines) in zip(axes, panels):
        for column, label, scale in lines:
            if column in log.columns:
                axis.plot(times, series(column, scale), linewidth=0.8, label=label)
        for time_s in slides:
            axis.axvline(time_s, color="0.6", linewidth=0.6, linestyle=":")
        for time_s in marks:
            axis.axvline(time_s, color="crimson", linewidth=1.2)
        axis.set_ylabel(f"{title} ({unit})")
        axis.grid(True, linewidth=0.3)
        if len(lines) > 1:
            axis.legend(loc="upper right", fontsize="small")
    axes[0].set_title(
        f"{log.path.name}: red lines are marks, dotted are slide-off starts"
    )
    axes[-1].set_xlabel("time (s)")
    target = log.path.with_suffix(".png")
    figure.savefig(target, dpi=110)
    print(f"Plot: {target}")


if __name__ == "__main__":
    sys.exit(main())
